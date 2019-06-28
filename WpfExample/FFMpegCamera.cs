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

        private byte[] BgrImage = null;

        private volatile bool mRunning = false;

        private long LastFrameTime = 0;
        private double fpssmoothed = -1.0;

        public FFMpegCamera()
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
                FFMpegCameraFrameHandler cameraFrameHandler = CameraFrame;
                if (cameraFrameHandler != null)
                {
                    if (BgrImage == null || BgrImage.Length != len)
                    {
                        BgrImage = new byte[len];
                    }

                    //byte[] bgrImage = FrameArrayPool.Rent(len);
                    Marshal.Copy(ptr, BgrImage, 0, len);
                    //the array must be copied (because it is re-used by wrapper next frame) but after copying this method
                    //should return ASAP because we're blocks camera from grabbing more frames.

                    //The FFMpegCameraFrameHandler instances should probably make their own copy of the array before returning
                    //(though in practice they don't *have* to as long as they finish with it quickly). If you want to do something
                    //with these frames that may take some time you should probably make a copy (ideally to an array that it reuses
                    //or one from an arraypool).

                    cameraFrameHandler(BgrImage, w, h);
                   
                    long timesince = DateTime.Now.Ticks - LastFrameTime;
                    LastFrameTime += timesince;
                    double ffps = 10000000.0 / (double)timesince;
                    fpssmoothed = fpssmoothed == -1.0 ? ffps : ((fpssmoothed * 5.0 + ffps) / 6.0);
                    Console.WriteLine("time: " + timesince + "\t  fps: " + ffps + "\t  fpssmoothed: " + fpssmoothed);
                }

            };
            CameraSource = ConfigurationManager.AppSettings["CameraSource"];
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

            CameraCodec = ConfigurationManager.AppSettings["CameraCodec"];
            int fps = Convert.ToInt32(ConfigurationManager.AppSettings["CameraFPS"]);
            CameraFPS = fps > 0 ? "" + fps : null;
            int resX = Convert.ToInt32(ConfigurationManager.AppSettings["CameraResX"]);
            int resY = Convert.ToInt32(ConfigurationManager.AppSettings["CameraResY"]);
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
