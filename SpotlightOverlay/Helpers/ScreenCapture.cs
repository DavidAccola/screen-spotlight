using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SpotlightOverlay.Helpers;

public static class ScreenCapture
{
    /// <summary>
    /// Captures a screenshot of the specified monitor bounds (in physical pixels).
    /// Returns a frozen BitmapSource suitable for use as a WPF ImageBrush.
    /// </summary>
    public static BitmapSource CaptureMonitor(Rect monitorBounds)
    {
        int x = (int)monitorBounds.X;
        int y = (int)monitorBounds.Y;
        int w = (int)monitorBounds.Width;
        int h = (int)monitorBounds.Height;

        using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
        }

        var bmpData = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Use 96 DPI — the ImageBrush with Stretch=Fill handles DIP-to-pixel mapping,
        // so the bitmap DPI metadata doesn't matter.
        var source = BitmapSource.Create(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
            bmpData.Scan0, bmpData.Stride * h, bmpData.Stride);

        bmp.UnlockBits(bmpData);
        source.Freeze();
        return source;
    }
}
