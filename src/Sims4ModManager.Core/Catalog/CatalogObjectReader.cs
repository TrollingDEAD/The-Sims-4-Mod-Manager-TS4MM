namespace Sims4ModManager.Core.Catalog;

/// <summary>
/// Reads the name key of a buy/build catalog entry (COBJ, type 0x319E4F1D). The entry starts with its
/// version, followed by the common catalog block: its own version, then the name and description keys.
/// </summary>
public static class CatalogObjectReader
{
    public const uint ResourceType = 0x319E4F1D;

    public static uint? TryReadNameKey(byte[] data)
    {
        if (data.Length < 12)
            return null;
        return BitConverter.ToUInt32(data, 8);
    }
}
