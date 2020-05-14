using System;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Threading;

namespace WpfExample
{
    public delegate void FFMpegCameraFrameHandler(byte[] bgrImage, int width, int height);

    public class FFMpegCamera
    {
        #region interop
        private delegate void ErrorCallback(IntPtr ptr, int len);

        private delegate void ImageCallback(IntPtr ptr, int len, int w, int h);

        [DllImport("FFMpegWrapper.dll")]
        private static extern void start(byte[] device_name, byte[] vcodec, byte[] framerate, byte[] video_size, bool show_video_device_dialog, bool no_convert, int crop_x, int crop_y, int crop_w, int crop_h, bool flip_h, bool flip_v, bool transpose, ErrorCallback errorCallback, ImageCallback imageCallback);

        [DllImport("FFMpegWrapper.dll")]
        private static extern void stop();

        [DllImport("FFMpegWrapper.dll")]
        private static extern int get_ds_video_input_devices([In, Out] string[] devices);
        #endregion

        public event FFMpegCameraFrameHandler CameraFrame;

        private readonly ErrorCallback MyErrorCallback;
        private readonly ImageCallback MyImageCallback;

        private readonly string CameraSource;
        private readonly string CameraCodec;
        private readonly string CameraFPS;
        private readonly string CameraRes;
        private readonly bool CameraShowSettings;
        private readonly int CameraCropX;
        private readonly int CameraCropY;
        private readonly int CameraCropW;
        private readonly int CameraCropH;
        private readonly bool CameraFlipH;
        private readonly bool CameraFlipV;
        private readonly bool CameraTranspose;

        private readonly object BgrImageLock = new object();
        private byte[] BgrImage = null;
        private int BgrImageW = -1;
        private int BgrImageH = -1;

        private volatile bool mRunning = false;

        private long LastFrameTime = 0;
        private double FpsSmoothed = -1.0;

        public FFMpegCamera()//TODO: this constructor is a bit big isn't it?
        {
            MyErrorCallback = (IntPtr ptr, int len) =>
            {
                byte[] ba = new byte[len];
                Marshal.Copy(ptr, ba, 0, len);
                Console.WriteLine("Error: " + System.Text.Encoding.UTF8.GetString(ba));
                Console.WriteLine("");
                if (mRunning)
                {
                    Thread thread = new Thread(new ThreadStart(() =>
                    {
                        Thread.Sleep(250);
                        if (mRunning)
                        {
                            Start(false);
                            if (!mRunning)
                            {
                                stop();
                            }
                        }
                    }));
                    thread.IsBackground = true;
                    thread.Start();
                }
            };

            MyImageCallback = (IntPtr ptr, int len, int w, int h) =>
            {
                PrintFPS();
                //wrapper may (though currently doesn't) supply bad parameters to signal camera stream is finished
                if (mRunning && ptr != null && len > 0 && w > 0 & h > 0)
                {
                    lock (BgrImageLock)
                    {
                        if (mRunning)//may have changed if we've been waiting for lock for a while
                        {
                            if (BgrImage == null || BgrImage.Length != len)
                            {
                                BgrImage = new byte[len];
                            }
                            Marshal.Copy(ptr, BgrImage, 0, len);
                            BgrImageW = w;
                            BgrImageH = h;

                            FFMpegCameraFrameHandler cameraFrame = CameraFrame;
                            if (null != cameraFrame)
                            {
                                //FFMpegCameraFrameHandler's should return quickly (because this callback blocks the wrapper from
                                //getting more frames), they also probably shouldn't use the array after they return (though in practice they
                                //can get away with using it for a brief period - as long as they are done before next time Marshal.Copy()
                                //line above is executed)

                                //anything that performs much work on the frame (and especially things that won't make use of every frame captured)
                                //should probably call GetCameraFrame rather than implementing a FFMpegCameraFrameHandler
                                cameraFrame(BgrImage, BgrImageW, BgrImageH);
                            }
                            Monitor.PulseAll(BgrImageLock);
                            return;
                        }
                    }
                }
                //we'll end up here if mRunning is false or if ffmpegwrappe called with bad parameters
                lock (BgrImageLock)
                {
                    BgrImage = null;
                    BgrImageW = -1;
                    BgrImageH = -1;
                    Monitor.PulseAll(BgrImageLock);
                }
            };

            CameraSource = ConfigurationManager.AppSettings["CameraSource"];
            if (CameraSource != null)
            {
                CameraSource = CameraSource.Trim();
                if (CameraSource.Length == 0)
                    CameraSource = null;
                else
                {
                    int index;
                    if (int.TryParse(CameraSource, out index))
                    {
                        string source = GetDeviceNameFromIndex(index);
                        if (source == null)
                        {
                            System.Windows.MessageBox.Show("Cannot find CameraSource: " + CameraSource, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                        else
                        {
                            CameraSource = source;
                        }
                    }
                }
            }

            CameraCodec = ConfigurationManager.AppSettings["CameraCodec"];
            int fps = GetIntSetting("CameraFPS");
            CameraFPS = fps > 0 ? "" + fps : null;
            int resX =  GetIntSetting("CameraResX");
            int resY = GetIntSetting("CameraResY");
            if (resX > 0 && resY > 0)
            {
                CameraRes = resX + "x" + resY;
            }
            else
            {
                CameraRes = null;
            }

            CameraShowSettings = Convert.ToBoolean(ConfigurationManager.AppSettings["CameraShowSettings"]);

            string[] cc = ConfigurationManager.AppSettings["CameraCrop"].Split(',');
            if (cc.Length == 4)
            {
                CameraCropX = Convert.ToInt32(cc[0]);
                CameraCropY = Convert.ToInt32(cc[1]);
                CameraCropW = Convert.ToInt32(cc[2]);
                CameraCropH = Convert.ToInt32(cc[3]);
            }
            else
            {
                CameraCropX = -1;
                CameraCropY = -1;
                CameraCropW = -1;
                CameraCropH = -1;
            }

            int cameraRotation = Convert.ToInt32(ConfigurationManager.AppSettings["CameraRotation"]);
            switch (cameraRotation)
            {
                case 90:
                    //flip happens before transpose (otherwise we'd be flipping H)
                    CameraFlipH = false;
                    CameraFlipV = true;
                    CameraTranspose = true;
                    break;
                case 180:
                    CameraFlipH = true;
                    CameraFlipV = true;
                    CameraTranspose = false;
                    break;
                case 270:
                    //flip happens before transpose (otherwise we'd be flipping V)
                    CameraFlipH = true;
                    CameraFlipV = false;
                    CameraTranspose = true;
                    break;
                default:
                    CameraFlipH = false;
                    CameraFlipV = false;
                    CameraTranspose = false;
                    break;
            }
        }

        private void PrintFPS()
        {
            long timesince = DateTime.Now.Ticks - LastFrameTime;
            LastFrameTime += timesince;
            double fps = 10000000.0 / (double)timesince;
            FpsSmoothed = FpsSmoothed == -1.0 ? fps : ((FpsSmoothed * 5.0 + fps) / 6.0);
            Console.WriteLine("time: " + timesince + "\t  fps: " + fps + "\t  FpsSmoothed: " + FpsSmoothed);
        }

        private static int GetIntSetting(string key)
        {
            string val = ConfigurationManager.AppSettings[key];
            int intval;
            if (int.TryParse(val, out intval))
            {
                return intval;
            }
            return -1;
        }

        private void Start(bool cameraShowSettings)
        {
            start(strToBA(CameraSource), strToBA(CameraCodec), strToBA(CameraFPS), strToBA(CameraRes), cameraShowSettings, false, CameraCropX, CameraCropY, CameraCropW, CameraCropH, CameraFlipH, CameraFlipV, CameraTranspose, MyErrorCallback, MyImageCallback);
        }

        public void Start()
        {
            mRunning = true;
            Start(CameraShowSettings);
        }

        public void Stop()
        {
            mRunning = false;
            stop();
            lock (BgrImageLock)
            {
                Monitor.PulseAll(BgrImageLock);//wake up anything stuck in a GetCameraFrame call
            }
        }

        public byte[] GetCameraFrame(out int width, out int height)
        {
            if (!mRunning) {
                width = -1;
                height = -1;
                return null;
            }
            byte[] retVal;
            lock (BgrImageLock)
            {
                while (BgrImage == null && mRunning)
                {
                    Monitor.Wait(BgrImageLock);
                }
                if (mRunning)
                {
                    retVal = BgrImage;
                    width = BgrImageW;
                    height = BgrImageH;
                }
                else
                {
                    retVal = null;
                    width = -1;
                    height = -1;
                }
                BgrImage = null;
                //the above not only ensures the same frame isn't returned twice
                //but that the byte array won't be re-used for next camera frame
                //(thereby potentially writing a new frame into it as it is still
                //being used).
                //ideally we should do something to manage the arrays we allocate
                //(eg: use an ArrayPool)
            }
            return retVal;
        }

        private static string GetDeviceNameFromIndex(int n)
        {
            if (n < 0)
            {
                return null;
            }
            string[] devices = new string[1024];
            int r = get_ds_video_input_devices(devices);
            return n > r ? null : devices[n];

        }

        private static byte[] strToBA(string s)
        {
            return s == null ? null : System.Text.Encoding.UTF8.GetBytes(s);
        }
    }
}
