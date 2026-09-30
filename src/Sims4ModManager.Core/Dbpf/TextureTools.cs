using System.IO.Compression;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using CommunityToolkit.HighPerformance;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Dbpf;

/// <summary>One oversized texture resource a package contains, and the size downscaling it would target.</summary>
public sealed record TextureCandidate(ResourceKey Key, int Width, int Height, int TargetWidth, int TargetHeight, uint StoredSize);

public sealed record TextureDownscaleResult(int Downscaled, int Skipped, long BytesBefore, long BytesAfter)
{
    public long Saved => BytesBefore - BytesAfter;
}

/// <summary>
/// Detects and downscales oversized CAS/object textures - many CC creators ship 4K textures the game
/// never benefits from at typical camera distances, which cost disk space and per-file scan time for
/// no visual gain. Only the resource type documented as a DDS/block-compressed texture
/// (<see cref="DdsTextureType"/>, see <c>ResourceTypeCatalog</c>) is ever touched, and only if
/// BCnEncoder.Net recognizes its compression format as well-formed; anything else is left untouched
/// rather than guessed at. Downscaling decodes the base mip, box-filters it down, regenerates a full
/// mip chain and re-encodes with the same block-compression format the original used.
/// </summary>
public static class TextureTools
{
    /// <summary>The DBPF resource type documented as a DDS-compressed texture.</summary>
    public const uint DdsTextureType = 0x00B2D882;

    /// <summary>Textures larger than this on either axis are flagged; 2K already exceeds what CAS/object cameras resolve.</summary>
    public const int RecommendedMaxDimension = 2048;

    /// <summary>Scans one package's DDS texture resources for ones exceeding <paramref name="maxDimension"/>.</summary>
    public static IReadOnlyList<TextureCandidate> Inspect(string path, IReadOnlyList<PackageResource> resources, int maxDimension)
    {
        var textures = resources.Where(r => r.Key.Type == DdsTextureType).ToList();
        var candidates = new List<TextureCandidate>();
        if (textures.Count == 0)
            return candidates;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var resource in textures)
            {
                if (!TryGetDimensions(stream, resource, out int width, out int height))
                    continue;
                if (width <= maxDimension && height <= maxDimension)
                    continue;
                var (targetWidth, targetHeight) = TargetSize(width, height, maxDimension);
                candidates.Add(new TextureCandidate(resource.Key, width, height, targetWidth, targetHeight, resource.StoredSize));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: nothing to flag */ }

        return candidates;
    }

    private static bool TryGetDimensions(Stream stream, PackageResource resource, out int width, out int height)
    {
        width = height = 0;
        try
        {
            byte[] bytes = DbpfReader.ReadResource(stream, resource);
            using var ms = new MemoryStream(bytes);
            var dds = DdsFile.Load(ms);
            width = (int)dds.header.dwWidth;
            height = (int)dds.header.dwHeight;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IndexOutOfRangeException or ArgumentException or OverflowException or FormatException)
        {
            return false; // not a well-formed DDS payload - leave it alone rather than guess
        }
    }

    internal static (int Width, int Height) TargetSize(int width, int height, int maxDimension)
    {
        int w = width, h = height;
        while (w > maxDimension || h > maxDimension)
        {
            w = Math.Max(4, w / 2);
            h = Math.Max(4, h / 2);
        }
        return (w, h);
    }

    /// <summary>
    /// Downscales one texture resource's decompressed bytes to at most <paramref name="maxDimension"/>
    /// on each axis. Returns null (leave untouched) if the resource isn't a well-formed DDS file
    /// BCnEncoder.Net recognizes, or is already within the limit.
    /// </summary>
    public static byte[]? DownscaleResource(byte[] ddsBytes, int maxDimension)
    {
        DdsFile dds;
        try
        {
            using var ms = new MemoryStream(ddsBytes);
            dds = DdsFile.Load(ms);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IndexOutOfRangeException or ArgumentException or OverflowException or FormatException)
        {
            return null;
        }

        var decoder = new BcDecoder();
        if (!decoder.IsSupportedFormat(dds) || decoder.IsHdrFormat(dds))
            return null;

        int width = (int)dds.header.dwWidth, height = (int)dds.header.dwHeight;
        var (targetWidth, targetHeight) = TargetSize(width, height, maxDimension);
        if (targetWidth == width && targetHeight == height)
            return null;

        var format = decoder.GetFormat(dds);
        var basePixels = decoder.Decode2D(dds);
        var resized = Resize(basePixels, targetWidth, targetHeight);

        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        var result = encoder.EncodeToDds(new Memory2D<ColorRgba32>(resized));

        using var outStream = new MemoryStream();
        result.Write(outStream);
        return outStream.ToArray();
    }

    /// <summary>Box-filter downscale (area-averaged, not nearest-pixel sampling) of a decoded base mip.</summary>
    private static ColorRgba32[,] Resize(Memory2D<ColorRgba32> source, int targetWidth, int targetHeight)
    {
        var span = source.Span;
        int sourceWidth = span.Width, sourceHeight = span.Height;
        var destination = new ColorRgba32[targetHeight, targetWidth];

        for (int y = 0; y < targetHeight; y++)
        {
            int sy0 = y * sourceHeight / targetHeight;
            int sy1 = Math.Max(sy0 + 1, (y + 1) * sourceHeight / targetHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                int sx0 = x * sourceWidth / targetWidth;
                int sx1 = Math.Max(sx0 + 1, (x + 1) * sourceWidth / targetWidth);

                long r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int sy = sy0; sy < sy1 && sy < sourceHeight; sy++)
                {
                    for (int sx = sx0; sx < sx1 && sx < sourceWidth; sx++)
                    {
                        var c = span[sy, sx];
                        r += c.r; g += c.g; b += c.b; a += c.a;
                        count++;
                    }
                }
                destination[y, x] = count == 0
                    ? span[Math.Min(sy0, sourceHeight - 1), Math.Min(sx0, sourceWidth - 1)]
                    : new ColorRgba32((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
            }
        }
        return destination;
    }

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> with every texture resource in <paramref name="keys"/>
    /// downscaled to at most <paramref name="maxDimension"/>; resources that turn out unsupported are
    /// copied through unchanged and counted as skipped. Every other resource is copied byte for byte.
    /// </summary>
    public static TextureDownscaleResult Downscale(string sourcePath, string destinationPath, IReadOnlySet<ResourceKey> keys, int maxDimension)
    {
        var resources = DbpfWriter.ReadIndexOrThrow(sourcePath);
        int downscaled = 0, skipped = 0;
        var entries = new List<PackageWriteEntry>(resources.Count);

        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            foreach (var resource in resources.OrderBy(r => r.ChunkOffset))
            {
                if (keys.Contains(resource.Key))
                {
                    byte[] raw = DbpfReader.ReadResource(input, resource);
                    byte[]? downscaledBytes = DownscaleResource(raw, maxDimension);
                    if (downscaledBytes is not null)
                    {
                        byte[] packed = Deflate(downscaledBytes);
                        entries.Add(PackageWriteEntry.FromBytes(resource.Key, packed, (uint)downscaledBytes.Length, DbpfReader.CompressionZlib));
                        downscaled++;
                        continue;
                    }
                    skipped++;
                }
                entries.Add(PackageWriteEntry.Copy(sourcePath, resource));
            }
        }

        DbpfWriter.Write(destinationPath, entries);
        return new TextureDownscaleResult(downscaled, skipped, new FileInfo(sourcePath).Length, new FileInfo(destinationPath).Length);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
            zlib.Write(data);
        return output.ToArray();
    }
}
