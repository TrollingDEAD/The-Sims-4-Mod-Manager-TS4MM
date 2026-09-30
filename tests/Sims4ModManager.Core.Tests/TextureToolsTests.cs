using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;
using CommunityToolkit.HighPerformance;
using Sims4ModManager.Core.Dbpf;
using Sims4ModManager.Core.Models;

namespace Sims4ModManager.Core.Tests;

public class TextureToolsTests : IDisposable
{
    private readonly string _root;

    public TextureToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "S4MM_Texture_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A real, well-formed BC1 DDS file with a full mip chain, built via BCnEncoder.Net itself.</summary>
    private static byte[] BuildDds(int size)
    {
        var pixels = new ColorRgba32[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y, x] = new ColorRgba32((byte)(x * 255 / size), (byte)(y * 255 / size), 128, 255);

        var encoder = new BcEncoder(CompressionFormat.Bc1);
        encoder.OutputOptions.GenerateMipMaps = true;
        var dds = encoder.EncodeToDds(new Memory2D<ColorRgba32>(pixels));

        using var ms = new MemoryStream();
        dds.Write(ms);
        return ms.ToArray();
    }

    [Fact]
    public void InspectFlagsTextureLargerThanLimit()
    {
        byte[] dds = BuildDds(32);
        string path = Path.Combine(_root, "big.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(TextureTools.DdsTextureType, 0, 1, dds) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        var candidates = TextureTools.Inspect(path, resources, maxDimension: 16);

        var candidate = Assert.Single(candidates);
        Assert.Equal(32, candidate.Width);
        Assert.Equal(32, candidate.Height);
        Assert.Equal(16, candidate.TargetWidth);
        Assert.Equal(16, candidate.TargetHeight);
    }

    [Fact]
    public void InspectIgnoresTextureWithinLimit()
    {
        byte[] dds = BuildDds(16);
        string path = Path.Combine(_root, "small.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(TextureTools.DdsTextureType, 0, 1, dds) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        Assert.Empty(TextureTools.Inspect(path, resources, maxDimension: 16));
    }

    [Fact]
    public void InspectIgnoresNonTextureResourceTypes()
    {
        string path = Path.Combine(_root, "other.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(0x12345678, 0, 1, new byte[4096]) }));
        var resources = DbpfReader.TryReadIndex(path)!;

        Assert.Empty(TextureTools.Inspect(path, resources, maxDimension: 16));
    }

    [Fact]
    public void DownscaleResourceProducesSmallerReadableDds()
    {
        byte[] dds = BuildDds(32);

        byte[]? result = TextureTools.DownscaleResource(dds, maxDimension: 16);

        Assert.NotNull(result);
        using var ms = new MemoryStream(result!);
        var loaded = DdsFile.Load(ms);
        Assert.Equal(16u, loaded.header.dwWidth);
        Assert.Equal(16u, loaded.header.dwHeight);
        Assert.True(new BcDecoder().IsSupportedFormat(loaded));
        Assert.True(result!.Length < dds.Length);
    }

    [Fact]
    public void DownscaleResourceReturnsNullForNonDdsBytes()
    {
        Assert.Null(TextureTools.DownscaleResource(new byte[] { 1, 2, 3, 4, 5 }, maxDimension: 16));
    }

    [Fact]
    public void DownscaleResourceReturnsNullWhenAlreadyWithinLimit()
    {
        byte[] dds = BuildDds(16);
        Assert.Null(TextureTools.DownscaleResource(dds, maxDimension: 16));
    }

    [Fact]
    public void DownscalePackageShrinksOnlyFlaggedTextureAndCopiesOthersUnchanged()
    {
        byte[] bigDds = BuildDds(32);
        var otherKey = new ResourceKey(0x12345678, 0, 2);
        string path = Path.Combine(_root, "mod.package");
        File.WriteAllBytes(path, TestDbpfBuilder.Build(new[]
        {
            new TestDbpfBuilder.Entry(TextureTools.DdsTextureType, 0, 1, bigDds),
            new TestDbpfBuilder.Entry(otherKey.Type, otherKey.Group, otherKey.Instance, new byte[] { 9, 9, 9 })
        }));

        var resources = DbpfReader.TryReadIndex(path)!;
        var textureKey = resources.Single(r => r.Key.Type == TextureTools.DdsTextureType).Key;
        string destination = Path.Combine(_root, "mod-downscaled.package");

        var result = TextureTools.Downscale(path, destination, new HashSet<ResourceKey> { textureKey }, maxDimension: 16);

        Assert.Equal(1, result.Downscaled);
        Assert.Equal(0, result.Skipped);
        Assert.True(result.Saved > 0);

        var newResources = DbpfReader.TryReadIndex(destination)!;
        Assert.Equal(2, newResources.Count);

        using var input = new FileStream(destination, FileMode.Open, FileAccess.Read);
        byte[] downscaledDds = DbpfReader.ReadResource(input, newResources.Single(r => r.Key.Type == TextureTools.DdsTextureType));
        using var ms = new MemoryStream(downscaledDds);
        var loaded = DdsFile.Load(ms);
        Assert.Equal(16u, loaded.header.dwWidth);

        byte[] otherBytes = DbpfReader.ReadResource(input, newResources.Single(r => r.Key.Type == otherKey.Type));
        Assert.Equal(new byte[] { 9, 9, 9 }, otherBytes);
    }
}
