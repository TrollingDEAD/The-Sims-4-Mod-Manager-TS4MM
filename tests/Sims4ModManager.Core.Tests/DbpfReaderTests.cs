using Sims4ModManager.Core.Dbpf;

namespace Sims4ModManager.Core.Tests;

public class DbpfReaderTests
{
    [Fact]
    public void ReadsUncompressedIndexEntries()
    {
        var entries = new[]
        {
            (Type: 0x0166038Cu, Group: 0x00000000u, Instance: 0x1122334455667788UL),
            (Type: 0x545AC67Au, Group: 0x00000001u, Instance: 0xAABBCCDDEEFF0011UL),
        };

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));

            var keys = DbpfReader.TryReadIndex(path)?.Select(r => r.Key).ToList();

            Assert.NotNull(keys);
            Assert.Equal(2, keys!.Count);
            Assert.Contains(keys, k => k.Type == entries[0].Type && k.Group == entries[0].Group && k.Instance == entries[0].Instance);
            Assert.Contains(keys, k => k.Type == entries[1].Type && k.Group == entries[1].Group && k.Instance == entries[1].Instance);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadsIndexWithConstantTypeGroupAndInstanceHigh()
    {
        var entries = new[]
        {
            (Type: 0x0166038Cu, Group: 0x00000042u, Instance: 0x0000000100000001UL),
            (Type: 0x0166038Cu, Group: 0x00000042u, Instance: 0x0000000100000002UL),
            (Type: 0x0166038Cu, Group: 0x00000042u, Instance: 0x0000000100000003UL),
        };

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, TestDbpfBuilder.Build(entries, indexFlags: 0x7));

            var keys = DbpfReader.TryReadIndex(path)?.Select(r => r.Key).ToList();

            Assert.NotNull(keys);
            Assert.Equal(3, keys!.Count);
            Assert.All(keys, k => Assert.Equal(0x0166038Cu, k.Type));
            Assert.All(keys, k => Assert.Equal(0x00000042u, k.Group));
            Assert.Contains(keys, k => k.Instance == entries[2].Instance);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullForNonDbpfFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Assert.Null(DbpfReader.TryReadIndex(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsEmptyForZeroEntryPackage()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, TestDbpfBuilder.Build(Array.Empty<(uint, uint, ulong)>()));
            var keys = DbpfReader.TryReadIndex(path)?.Select(r => r.Key).ToList();
            Assert.NotNull(keys);
            Assert.Empty(keys!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SkipsDeletedIndexEntries()
    {
        var entries = new[]
        {
            new TestDbpfBuilder.Entry(1, 0, 1, new byte[] { 1 }),
            new TestDbpfBuilder.Entry(1, 0, 2, new byte[] { 2 }, TestDbpfBuilder.Deleted),
        };

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));

            var resources = DbpfReader.TryReadIndex(path);

            Assert.NotNull(resources);
            var single = Assert.Single(resources!);
            Assert.Equal(1UL, single.Key.Instance);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReturnsNullWhenEntryCountExceedsFileSize()
    {
        var bytes = TestDbpfBuilder.Build(new[] { new TestDbpfBuilder.Entry(1, 0, 1) });
        BitConverter.GetBytes(50_000_000u).CopyTo(bytes, 0x24); // corrupt indexEntryCount

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Null(DbpfReader.TryReadIndex(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HashesMatchForSameContentRegardlessOfZlibCompression()
    {
        byte[] content = Enumerable.Range(0, 2000).Select(i => (byte)(i % 7)).ToArray();
        byte[] other = content.Select(b => (byte)(b + 1)).ToArray();

        var entries = new[]
        {
            new TestDbpfBuilder.Entry(1, 0, 1, content),
            new TestDbpfBuilder.Entry(1, 0, 2, content, TestDbpfBuilder.Zlib),
            new TestDbpfBuilder.Entry(1, 0, 3, other),
        };

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, TestDbpfBuilder.Build(entries));
            var resources = DbpfReader.TryReadIndex(path)!;

            var hashes = DbpfReader.TryHashResources(path, resources, decompress: true);

            Assert.Equal(3, hashes.Count);
            Assert.All(hashes.Values, Assert.NotNull);
            Assert.Equal(hashes[resources[0]], hashes[resources[1]]);
            Assert.NotEqual(hashes[resources[0]], hashes[resources[2]]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
