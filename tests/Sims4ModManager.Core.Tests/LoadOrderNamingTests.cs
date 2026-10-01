using Sims4ModManager.Core.LoadOrder;

namespace Sims4ModManager.Core.Tests;

public class LoadOrderNamingTests
{
    [Theory]
    [InlineData("000_Name.package", 0, "Name.package")]
    [InlineData("010-Name.package", 10, "Name.package")]
    [InlineData("42 Name.package", 42, "Name.package")]
    [InlineData("1234_Name", 1234, "Name")]
    public void TryParsePrefixRecognizesConventions(string name, int expectedNumber, string expectedRemainder)
    {
        var parsed = LoadOrderNaming.TryParsePrefix(name);

        Assert.NotNull(parsed);
        Assert.Equal(expectedNumber, parsed!.Value.Number);
        Assert.Equal(expectedRemainder, parsed.Value.Remainder);
    }

    [Theory]
    [InlineData("Name.package")]
    [InlineData("12345_Name.package")] // more than 4 digits before the separator: not recognized
    [InlineData("ZZZ_Override.package")] // letters, not digits
    public void TryParsePrefixReturnsNullForUnrecognizedNames(string name)
    {
        Assert.Null(LoadOrderNaming.TryParsePrefix(name));
    }

    [Fact]
    public void StripPrefixRemovesOnlyARecognizedPrefix()
    {
        Assert.Equal("Name.package", LoadOrderNaming.StripPrefix("000_Name.package"));
        Assert.Equal("Name.package", LoadOrderNaming.StripPrefix("Name.package"));
    }

    [Fact]
    public void FormatWithPrefixZeroPadsToThreeDigits()
    {
        Assert.Equal("005_Name.package", LoadOrderNaming.FormatWithPrefix(5, "Name.package"));
        Assert.Equal("050_Name.package", LoadOrderNaming.FormatWithPrefix(50, "Name.package"));
        Assert.Equal("1234_Name.package", LoadOrderNaming.FormatWithPrefix(1234, "Name.package"));
    }
}
