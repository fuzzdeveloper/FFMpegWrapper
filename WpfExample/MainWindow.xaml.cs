using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WpfExample
{
    public partial class MainWindow : Window
    {
        private static readonly bool USE_FRAME_HANDLER = true;
        private readonly FFMpegCamera MyFFMpegCamera;
        private WriteableBitmap DisplayedBitmap = null;
        private volatile bool GrabCameraFrameThreadRunning = false;

        public MainWindow()
        {
            InitializeComponent();
            MyFFMpegCamera = new FFMpegCamera();
        }

        public void CameraFrame(byte[] bgrImage, int bgrWidth, int bgrHeight)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                //the below 2 lines make a Bitmap from bgrImage *without* copying data
                //however the GCHandle needs to be held until the Bitmap is no longer in use
                //GCHandle gcHandle = GCHandle.Alloc((object)bgrImage, GCHandleType.Pinned);
                //Bitmap bitmap = new Bitmap(bgrWidth, bgrHeight, bgrWidth * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, gcHandle.AddrOfPinnedObject());
                //...do stuff... bear in mind changes to bitmap will change the byte array too
                //bitmap.Dispose();
                //gcHandle.Free();

                //Whilst I haven't found a way to make an ImageSource without copying the frame, using a single WriteableBitmap is a lot
                //better than the alternatives I found:
                //- solutions involving Imaging.CreateBitmapSourceFromHBitmap() spike memory usage and rely heavily on GC.
                //- solutions involving saving image as a Bitmap to MemoryStream which is then used to make BitmapImage
                //  are kinder on memory but worse on CPU.
                if (DisplayedBitmap == null)
                {
                    DisplayedBitmap = new WriteableBitmap(bgrWidth, bgrHeight, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
                    ImageFrame.Source = DisplayedBitmap;
                }
                DisplayedBitmap.Lock();
                Marshal.Copy(bgrImage, 0, DisplayedBitmap.BackBuffer, bgrHeight * bgrWidth * 3);
                DisplayedBitmap.AddDirtyRect(new Int32Rect(0, 0, bgrWidth, bgrHeight));
                DisplayedBitmap.Unlock();
            }));
        }

        private void GrabCameraFrameLoop()
        {
            byte[] bgrImage;
            int bgrWidth;
            int bgrHeight;
            while (GrabCameraFrameThreadRunning)
            {
                bgrImage = MyFFMpegCamera.GetCameraFrame(out bgrWidth, out bgrHeight);
                if (bgrImage == null || bgrWidth <= 0 || bgrHeight <= 0)
                {
                    break;
                }
                CameraFrame(bgrImage, bgrWidth, bgrHeight);
            }
        }

        private void Window_Loaded(object sender, EventArgs e)
        {
            //you can start camera in constructor too, the wrapper isn't as sensitive to how it is started as EMGU/OpenCV
            if (USE_FRAME_HANDLER)
            {
                MyFFMpegCamera.CameraFrame += CameraFrame;
            }
            else
            {
                GrabCameraFrameThreadRunning = true;
                Thread thread = new Thread(new ThreadStart(GrabCameraFrameLoop));
                thread.IsBackground = true;
                thread.Start();
            }
            MyFFMpegCamera.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (USE_FRAME_HANDLER)
            {
                MyFFMpegCamera.CameraFrame -= CameraFrame;
            }
            else
            {
                GrabCameraFrameThreadRunning = false;
            }
            MyFFMpegCamera.Stop();
        }
    }
}
