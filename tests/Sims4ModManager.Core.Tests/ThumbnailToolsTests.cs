using BitMiracle.LibJpeg;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tests;

public class ThumbnailToolsTests : IDisposable
{
    private readonly string _root;

    public ThumbnailToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Thumbnail_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A real, well-formed opaque RGB JPEG, built via BitMiracle.LibJpeg.NET itself.</summary>
    private static byte[] BuildJpeg(int size)
    {
        var rows = new SampleRow[size];
        for (int y = 0; y < size; y++)
        {
            byte[] data = new byte[size * 3];
            for (int x = 0; x < size; x++)
            {
                data[x * 3 + 0] = (byte)(x * 255 / size);
                data[x * 3 + 1] = (byte)(y * 255 / size);
                data[x * 3 + 2] = 128;
            }
            rows[y] = new SampleRow(data, size, 8, 3);
        }
        using var image = new JpegImage(rows, Colorspace.RGB);
        using var ms = new MemoryStream();
        image.WriteJpeg(ms, new CompressionParameters { Quality = 85 });
        return ms.ToArray();
    }

    /// <summary>The undocumented "ALFA" tag <c>ThumbnailLoader</c> looks for, prefixed before a real JPEG.</summary>
    private static byte[] WithFakeAlphaSegment(byte[] jpeg)
    {
        byte[] tag = "ALFA"u8.ToArray();
        var result = new byte[tag.Length + jpeg.Length];
        tag.CopyTo(result, 0);
        jpeg.CopyTo(result, tag.Length);
        return result;
    }

    [Fact]
    public void InspectFlagsThumbnailLargerThanLimit()
    {
        byte[] jpeg = BuildJpeg(32);
        string path = Path.Combine(_root, "big.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(ThumbnailTools.CasThumbnail, 0, 1, jpeg) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        var candidates = ThumbnailTools.Inspect(path, resources, maxDimension: 16);

        var candidate = Assert.Single(candidates);
        Assert.Equal(32, candidate.Width);
        Assert.Equal(16, candidate.TargetWidth);
    }

    [Fact]
    public void InspectIgnoresThumbnailWithinLimit()
    {
        byte[] jpeg = BuildJpeg(16);
        string path = Path.Combine(_root, "small.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(ThumbnailTools.CasThumbnail, 0, 1, jpeg) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        Assert.Empty(ThumbnailTools.Inspect(path, resources, maxDimension: 16));
    }

    [Fact]
    public void InspectIgnoresNonThumbnailResourceTypes()
    {
        string path = Path.Combine(_root, "other.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(0x12345678, 0, 1, BuildJpeg(32)) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        Assert.Empty(ThumbnailTools.Inspect(path, resources, maxDimension: 16));
    }

    [Fact]
    public void InspectSkipsThumbnailsWithAlphaSegment()
    {
        byte[] jpeg = WithFakeAlphaSegment(BuildJpeg(32));
        string path = Path.Combine(_root, "alpha.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(ThumbnailTools.CasThumbnail, 0, 1, jpeg) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        Assert.Empty(ThumbnailTools.Inspect(path, resources, maxDimension: 16));
    }

    [Fact]
    public void DownscaleResourceProducesSmallerReadableJpeg()
    {
        byte[] jpeg = BuildJpeg(32);

        byte[]? result = ThumbnailTools.DownscaleResource(jpeg, maxDimension: 16);

        Assert.NotNull(result);
        using var ms = new MemoryStream(result!);
        using var image = new JpegImage(ms);
        Assert.Equal(16, image.Width);
        Assert.Equal(16, image.Height);
        Assert.True(result!.Length < jpeg.Length);
    }

    [Fact]
    public void DownscaleResourceReturnsNullForAlphaSegment()
    {
        byte[] jpeg = WithFakeAlphaSegment(BuildJpeg(32));
        Assert.Null(ThumbnailTools.DownscaleResource(jpeg, maxDimension: 16));
    }

    [Fact]
    public void DownscaleResourceReturnsNullForNonJpegBytes()
    {
        Assert.Null(ThumbnailTools.DownscaleResource(new byte[] { 1, 2, 3, 4, 5 }, maxDimension: 16));
    }

    [Fact]
    public void DownscaleResourceReturnsNullWhenAlreadyWithinLimit()
    {
        byte[] jpeg = BuildJpeg(16);
        Assert.Null(ThumbnailTools.DownscaleResource(jpeg, maxDimension: 16));
    }

    [Fact]
    public void DownscalePackageShrinksOnlyFlaggedThumbnailAndCopiesOthersUnchanged()
    {
        byte[] bigJpeg = BuildJpeg(32);
        var otherKey = new ResourceKey(0x12345678, 0, 2);
        string path = Path.Combine(_root, "mod.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[]
        {
            new TestDbpfBuilder.Entry(ThumbnailTools.CasThumbnail, 0, 1, bigJpeg),
            new TestDbpfBuilder.Entry(otherKey.Type, otherKey.Group, otherKey.Instance, new byte[] { 9, 9, 9 })
        }));

        var resources = DbpfReader.TryReadIndex(path)!;
        var thumbnailKey = resources.Single(r => r.Key.Type == ThumbnailTools.CasThumbnail).Key;
        string destination = Path.Combine(_root, "mod-downscaled.package");

        var result = ThumbnailTools.Downscale(path, destination, new HashSet<ResourceKey> { thumbnailKey }, maxDimension: 16);

        Assert.Equal(1, result.Downscaled);
        Assert.Equal(0, result.Skipped);
        Assert.True(result.Saved > 0);

        var newResources = DbpfReader.TryReadIndex(destination)!;
        Assert.Equal(2, newResources.Count);

        using var input = new FileStream(destination, FileMode.Open, FileAccess.Read);
        byte[] downscaledJpeg = DbpfReader.ReadResource(input, newResources.Single(r => r.Key.Type == ThumbnailTools.CasThumbnail));
        using var image = new JpegImage(new MemoryStream(downscaledJpeg));
        Assert.Equal(16, image.Width);

        byte[] otherBytes = DbpfReader.ReadResource(input, newResources.Single(r => r.Key.Type == otherKey.Type));
        Assert.Equal(new byte[] { 9, 9, 9 }, otherBytes);
    }
}
