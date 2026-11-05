using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace StepExport.UI
{
    /// <summary>The ribbon icon: the STEP cube (embedded image), at the size of the ribbon slot and the display scale.</summary>
    internal static class Icons
    {
        private sealed class PictureConverter : AxHost
        {
            private PictureConverter() : base(string.Empty) { }
            public static object ToPictureDisp(Image image) => GetIPictureDispFromPicture(image);
        }

        public static object CreateStep(int size)
        {
            float scale = 1f;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, g.DpiX / 96f); } catch { }
            int px = (int)Math.Round(size * scale);

            var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StepExport.step.png"))
            using (var logo = Image.FromStream(stream))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(logo, new Rectangle(0, 0, px, px));
            }
            return PictureConverter.ToPictureDisp(bmp);
        }
    }
}
