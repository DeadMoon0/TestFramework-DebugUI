using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;
using System.Drawing;

namespace WpfCommon
{
    public static class Helper
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);

        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        public static ImageSource ImageSourceFromBitmap(byte[] bytes)
        {
            if(bytes.Length == 0) bytes = Properties.Resources.Null;
            using MemoryStream ms = new MemoryStream(bytes);
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(ms);
            return ImageSourceFromBitmap(bmp);
        }

        public static ImageSource ImageSourceFromBitmap(Bitmap bmp)
        {
            IntPtr hBitmap = bmp.GetHbitmap();
            BitmapSource s;
            try
            {
                s = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
            s.Freeze();
            return s;
        }

        public static Bitmap BitmapSourceToBitmap(BitmapSource bitmapSource)
        { 
            // Create a BitmapEncoder to encode the BitmapSource to a MemoryStream
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using (var memoryStream = new MemoryStream())
            {
                // Save the BitmapSource to the MemoryStream
                encoder.Save(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);

                // Create a Bitmap from the MemoryStream
                return new Bitmap(memoryStream);
            }
        }
    }
}