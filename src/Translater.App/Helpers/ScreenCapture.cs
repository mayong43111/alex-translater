using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Translater_App.Helpers;

/// <summary>
/// Captures the entire screen using GDI+.
/// </summary>
public static class ScreenCapture
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>
    /// Captures the entire virtual screen (all monitors) as a PNG byte array.
    /// </summary>
    public static byte[] CaptureFullScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// Crops a region from a full-screen PNG byte array.
    /// </summary>
    public static byte[] CropRegion(byte[] fullScreenPng, int cropX, int cropY, int cropWidth, int cropHeight)
    {
        using var ms = new MemoryStream(fullScreenPng);
        using var fullBitmap = new Bitmap(ms);

        // Clamp
        cropX = Math.Max(0, cropX);
        cropY = Math.Max(0, cropY);
        cropWidth = Math.Min(cropWidth, fullBitmap.Width - cropX);
        cropHeight = Math.Min(cropHeight, fullBitmap.Height - cropY);

        if (cropWidth <= 0 || cropHeight <= 0)
            return fullScreenPng;

        var rect = new Rectangle(cropX, cropY, cropWidth, cropHeight);
        using var cropped = fullBitmap.Clone(rect, fullBitmap.PixelFormat);

        using var outMs = new MemoryStream();
        cropped.Save(outMs, ImageFormat.Png);
        return outMs.ToArray();
    }
}
