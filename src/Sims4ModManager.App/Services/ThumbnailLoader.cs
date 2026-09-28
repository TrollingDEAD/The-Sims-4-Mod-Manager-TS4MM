using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sims4ModManager.App.Services;

/// <summary>
/// Decodes preview images extracted from packages. Sims 4 thumbnails are JPEGs that carry their
/// transparency as a PNG in an extra "ALFA" segment right after the JFIF header; it is applied here, so
/// hair and clothes appear cut out instead of on a black square.
/// </summary>
public static class ThumbnailLoader
{
    private const int DecodeSize = 160;
    private static readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Task<BitmapSource?> LoadAsync(string path) => Task.Run(() => Load(path));

    public static BitmapSource? Load(string path)
    {
        if (Cache.TryGetValue(path, out var weak) && weak.TryGetTarget(out var cached))
            return cached;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            var image = Decode(data);
            if (image is not null)
                Cache[path] = new WeakReference<BitmapSource>(image);
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or InvalidOperationException or FileFormatException)
        {
            return null;
        }
    }

    private static BitmapSource? Decode(byte[] data)
    {
        var color = DecodeFrame(data);
        if (color is null)
            return null;

        byte[]? alphaPng = ExtractAlpha(data);
        var alpha = alphaPng is null ? null : DecodeFrame(alphaPng);
        if (alpha is null || alpha.PixelWidth != color.PixelWidth || alpha.PixelHeight != color.PixelHeight)
        {
            color.Freeze();
            return color;
        }

        int width = color.PixelWidth, height = color.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        new FormatConvertedBitmap(color, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
        var mask = new byte[width * height];
        new FormatConvertedBitmap(alpha, PixelFormats.Gray8, null, 0).CopyPixels(mask, width, 0);
        for (int i = 0; i < mask.Length; i++)
            pixels[i * 4 + 3] = mask[i];

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static BitmapSource? DecodeFrame(byte[] data)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = DecodeSize;
        bitmap.StreamSource = new MemoryStream(data);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The PNG inside the "ALFA" segment (big-endian length after the tag), if any.</summary>
    private static byte[]? ExtractAlpha(byte[] data)
    {
        for (int i = 0; i + 8 < data.Length && i < 64; i++)
        {
            if (data[i] != 'A' || data[i + 1] != 'L' || data[i + 2] != 'F' || data[i + 3] != 'A')
                continue;
            int length = (data[i + 4] << 24) | (data[i + 5] << 16) | (data[i + 6] << 8) | data[i + 7];
            if (length <= 0 || i + 8 + length > data.Length)
                return null;
            return data.AsSpan(i + 8, length).ToArray();
        }
        return null;
    }
}
