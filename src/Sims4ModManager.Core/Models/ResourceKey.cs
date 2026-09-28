namespace Sims4ModManager.Core.Models;

/// <summary>
/// Type/Group/Instance triple that identifies a single resource inside a DBPF package.
/// Two packages that expose the same key both try to control the same game resource.
/// </summary>
public readonly record struct ResourceKey(uint Type, uint Group, ulong Instance)
{
    public override string ToString() => $"{Type:X8}-{Group:X8}-{Instance:X16}";

    /// <summary>Parses the <see cref="ToString"/> form ("TTTTTTTT-GGGGGGGG-IIIIIIIIIIIIIIII").</summary>
    public static ResourceKey? TryParse(string text)
    {
        var parts = text.Split('-');
        const System.Globalization.NumberStyles hex = System.Globalization.NumberStyles.HexNumber;
        return parts.Length == 3
               && uint.TryParse(parts[0], hex, null, out uint type)
               && uint.TryParse(parts[1], hex, null, out uint group)
               && ulong.TryParse(parts[2], hex, null, out ulong instance)
            ? new ResourceKey(type, group, instance)
            : null;
    }
}
