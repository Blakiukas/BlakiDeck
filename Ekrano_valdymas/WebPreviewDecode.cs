using System.IO;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;

internal static class WebPreviewDecode
{
    internal static int Bucket(double physicalPixels)
        => Math.Max(64, (int)Math.Ceiling(Math.Clamp(double.IsFinite(physicalPixels) ? physicalPixels : 64, 1, 16384) / 64) * 64);

    // UniformToFill requires enough pixels in both directions before cropping.
    // Do not upscale an original or change the PNG retained on disk.
    internal static (int Width, int Height) Size(int sourceWidth, int sourceHeight, int width, int height)
    {
        double ratio = Math.Min(1, Math.Max((double)width / sourceWidth, (double)height / sourceHeight));
        return (Math.Max(1, (int)Math.Ceiling(sourceWidth * ratio)), Math.Max(1, (int)Math.Ceiling(sourceHeight * ratio)));
    }

    internal static BitmapSource? Load(string path, int width, int height)
    {
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            int sourceWidth, sourceHeight;
            using (var metadata = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(metadata, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                sourceWidth = decoder.Frames[0].PixelWidth;
                sourceHeight = decoder.Frames[0].PixelHeight;
            }
            var size = Size(sourceWidth, sourceHeight, width, height);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = size.Width;
            image.DecodePixelHeight = size.Height;
            image.StreamSource = input;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
