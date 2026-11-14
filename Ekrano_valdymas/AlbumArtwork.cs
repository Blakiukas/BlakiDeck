using System.IO;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;

internal static class AlbumArtwork
{
    internal static async Task<BitmapSource?> ReadSessionThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null) return null;
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var random = await thumbnail.OpenReadAsync().AsTask(timeout.Token);
        if (random.Size == 0) return null;
        using var input = random.AsStreamForRead();
        using var copy = new MemoryStream();
        await input.CopyToAsync(copy, timeout.Token);
        copy.Position = 0;
        return await Task.Run(() => Decode(copy, MaximumPixels), timeout.Token);
    }
    // Covers are small UI surfaces, not full-resolution photo viewers.
    // The Normal cap retains ample headroom for the current player and DPI.
    internal static BitmapSource Decode(Stream stream,int maximumPixels)
    {
        long origin=stream.Position;
        int width,height;
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None);
        width=decoder.Frames[0].PixelWidth;height=decoder.Frames[0].PixelHeight;
        stream.Position=origin;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;
        if(Math.Max(width,height)>maximumPixels){if(width>=height)image.DecodePixelWidth=maximumPixels;else image.DecodePixelHeight=maximumPixels;}
        image.EndInit();
        // BitmapImage.StreamSource would retain the original compressed bytes,
        // even after disposing a MemoryStream. Publish only bounded decoded pixels.
        int stride=checked((image.PixelWidth*image.Format.BitsPerPixel+7)/8);
        var pixels=new byte[checked(stride*image.PixelHeight)];image.CopyPixels(pixels,stride,0);
        var result=BitmapSource.Create(image.PixelWidth,image.PixelHeight,image.DpiX,image.DpiY,image.Format,image.Palette,pixels,stride);
        result.Freeze();return result;
    }
    internal static int MaximumPixels=>1024;
}
