using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WpfExample
{
    public partial class MainWindow : Window
    {
        private readonly FFMpegCamera MyFFMpegCamera;
        private WriteableBitmap DisplayedBitmap = null;

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

        private void Window_Loaded(object sender, EventArgs e)
        {
            //you can do this in constructor too
            MyFFMpegCamera.CameraFrame += CameraFrame;
            MyFFMpegCamera.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MyFFMpegCamera.CameraFrame -= CameraFrame;
            MyFFMpegCamera.Stop();
        }
    }
}
