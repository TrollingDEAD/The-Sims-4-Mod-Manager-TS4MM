using System.IO.Compression;
using BitMiracle.LibJpeg;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Dbpf;

/// <summary>One oversized catalog thumbnail resource, and the size downscaling it would target.</summary>
public sealed record ThumbnailCandidate(ResourceKey Key, int Width, int Height, int TargetWidth, int TargetHeight, uint StoredSize);

public sealed record ThumbnailDownscaleResult(int Downscaled, int Skipped, long BytesBefore, long BytesAfter)
{
    public long Saved => BytesBefore - BytesAfter;
}

/// <summary>
/// Detects and downscales oversized CAS/Build-Buy catalog preview thumbnails - the optional, creator-
/// supplied JPEG image shown in the in-game catalog grid (and, via <c>Catalog.CatalogScanner</c>, in
/// this app's own Catalog tab). Some creators embed unnecessarily large previews; Sims 4 Studio's
/// "Delete CC thumbnails" batch fix removes them outright, but that would also blank this app's own
/// preview for that mod - downscaling instead keeps both working, just at a smaller size.
/// <para>
/// A thumbnail that carries the undocumented "ALFA" transparency segment <c>ThumbnailLoader</c> reads
/// (used by hair/clothes previews cut out against a background) is left untouched: this codebase only
/// has decode logic for that scheme, reverse-engineered from observed files, with nothing to verify a
/// reconstruction against - not a well-formed-enough container to risk rewriting.
/// </para>
/// </summary>
public static class ThumbnailTools
{
    /// <summary>The same three catalog-thumbnail resource types <c>Catalog.CatalogScanner</c> already reads.</summary>
    public const uint CasThumbnail = 0x3C1AF1F2;
    public const uint ObjectThumbnail = 0x3C2A8647;
    public const uint OtherThumbnail = 0x5B282D45;

    private static readonly HashSet<uint> ThumbnailTypes = new() { CasThumbnail, ObjectThumbnail, OtherThumbnail };

    /// <summary>Thumbnails larger than this on either axis are flagged; the in-game catalog grid renders them far smaller.</summary>
    public const int RecommendedMaxDimension = 256;

    /// <summary>The JPEG quality used when re-encoding (matches what most creator tools already export at).</summary>
    private const int JpegQuality = 85;

    /// <summary>Scans one package's catalog thumbnail resources for opaque ones exceeding <paramref name="maxDimension"/>.</summary>
    public static IReadOnlyList<ThumbnailCandidate> Inspect(string path, IReadOnlyList<PackageResource> resources, int maxDimension)
    {
        var thumbnails = resources.Where(r => ThumbnailTypes.Contains(r.Key.Type)).ToList();
        var candidates = new List<ThumbnailCandidate>();
        if (thumbnails.Count == 0)
            return candidates;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var resource in thumbnails)
            {
                if (!TryGetDimensions(stream, resource, out int width, out int height))
                    continue;
                if (width <= maxDimension && height <= maxDimension)
                    continue;
                var (targetWidth, targetHeight) = TextureTools.TargetSize(width, height, maxDimension);
                candidates.Add(new ThumbnailCandidate(resource.Key, width, height, targetWidth, targetHeight, resource.StoredSize));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: nothing to flag */ }

        return candidates;
    }

    private static bool TryGetDimensions(Stream stream, PackageResource resource, out int width, out int height)
    {
        width = height = 0;
        byte[] bytes;
        try { bytes = DbpfReader.ReadResource(stream, resource); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { return false; }

        if (HasAlphaSegment(bytes))
            return false; // transparency scheme this codebase can only decode, not safely reconstruct

        try
        {
            using var ms = new MemoryStream(bytes);
            using var image = new JpegImage(ms);
            width = image.Width;
            height = image.Height;
            return true;
        }
        catch (Exception)
        {
            // BitMiracle.LibJpeg.NET signals a malformed JPEG with plain System.Exception/
            // NotImplementedException rather than a specific type - a malformed thumbnail is routine,
            // untrusted input here, so any decode failure just means "leave it alone".
            return false;
        }
    }

    /// <summary>
    /// Downscales one thumbnail's decompressed JPEG bytes to at most <paramref name="maxDimension"/> on
    /// each axis via a box-filter resize. Returns null (leave untouched) if the bytes aren't a decodable
    /// JPEG, carry the "ALFA" transparency segment, or are already within the limit.
    /// </summary>
    public static byte[]? DownscaleResource(byte[] jpegBytes, int maxDimension)
    {
        if (HasAlphaSegment(jpegBytes))
            return null;

        JpegImage source;
        try
        {
            using var ms = new MemoryStream(jpegBytes);
            source = new JpegImage(ms);
        }
        catch (Exception)
        {
            return null;
        }

        using (source)
        {
            var (targetWidth, targetHeight) = TextureTools.TargetSize(source.Width, source.Height, maxDimension);
            if (targetWidth == source.Width && targetHeight == source.Height)
                return null;

            var resizedRows = Resize(source, targetWidth, targetHeight);
            using var resized = new JpegImage(resizedRows, source.Colorspace);
            using var outStream = new MemoryStream();
            resized.WriteJpeg(outStream, new CompressionParameters { Quality = JpegQuality });
            return outStream.ToArray();
        }
    }

    /// <summary>Box-filter downscale (area-averaged, not nearest-pixel sampling) of a decoded JPEG.</summary>
    private static SampleRow[] Resize(JpegImage source, int targetWidth, int targetHeight)
    {
        int sourceWidth = source.Width, sourceHeight = source.Height;
        int components = source.ComponentsPerSample;
        var rows = new SampleRow[targetHeight];

        var sum = new long[components];
        for (int y = 0; y < targetHeight; y++)
        {
            int sy0 = y * sourceHeight / targetHeight;
            int sy1 = Math.Max(sy0 + 1, (y + 1) * sourceHeight / targetHeight);
            var data = new byte[targetWidth * components];

            for (int x = 0; x < targetWidth; x++)
            {
                int sx0 = x * sourceWidth / targetWidth;
                int sx1 = Math.Max(sx0 + 1, (x + 1) * sourceWidth / targetWidth);

                Array.Clear(sum);
                int count = 0;
                for (int sy = sy0; sy < sy1 && sy < sourceHeight; sy++)
                {
                    var row = source.GetRow(sy);
                    for (int sx = sx0; sx < sx1 && sx < sourceWidth; sx++)
                    {
                        var sample = row[sx];
                        for (int c = 0; c < components; c++)
                            sum[c] += sample[c];
                        count++;
                    }
                }
                for (int c = 0; c < components; c++)
                    data[x * components + c] = count == 0 ? (byte)0 : (byte)(sum[c] / count);
            }
            rows[y] = new SampleRow(data, targetWidth, source.BitsPerComponent, (byte)components);
        }
        return rows;
    }

    /// <summary>
    /// The PNG-in-JPEG "ALFA" tag <c>ThumbnailLoader</c> looks for (Maxis' undocumented transparency
    /// scheme for CAS/Build-Buy preview cutouts), mirrored here only far enough to detect it, not decode it.
    /// </summary>
    private static bool HasAlphaSegment(byte[] data)
    {
        for (int i = 0; i + 3 < data.Length && i < 64; i++)
        {
            if (data[i] == 'A' && data[i + 1] == 'L' && data[i + 2] == 'F' && data[i + 3] == 'A')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Writes a copy of <paramref name="sourcePath"/> with every thumbnail resource in <paramref name="keys"/>
    /// downscaled to at most <paramref name="maxDimension"/>; resources that turn out unsupported are
    /// copied through unchanged and counted as skipped. Every other resource is copied byte for byte.
    /// </summary>
    public static ThumbnailDownscaleResult Downscale(string sourcePath, string destinationPath, IReadOnlySet<ResourceKey> keys, int maxDimension)
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
        return new ThumbnailDownscaleResult(downscaled, skipped, new FileInfo(sourcePath).Length, new FileInfo(destinationPath).Length);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
            zlib.Write(data);
        return output.ToArray();
    }
}
