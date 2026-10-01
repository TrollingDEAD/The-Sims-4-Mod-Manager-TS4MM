using Sims4ModManager.Core.LoadOrder;

namespace Sims4ModManager.Core.Tests;

public class LoadOrderReorderPlannerTests
{
    private const string Dir = @"C:\Mods";

    private static ReorderItem Item(string baseName, int? number) =>
        new(Path.Combine(Dir, number is { } n ? LoadOrderNaming.FormatWithPrefix(n, baseName) : baseName),
            number is { } n2 ? LoadOrderNaming.FormatWithPrefix(n2, baseName) : baseName, number);

    [Fact]
    public void MovingToTheEndWithRoomRenamesOnlyTheMovedItem()
    {
        var a = Item("A.package", 0);
        var b = Item("B.package", 10);
        var c = Item("C.package", 20);

        var renames = LoadOrderReorderPlanner.Plan(new[] { a, b, c }, fromIndex: 0, toIndex: 2);

        var rename = Assert.Single(renames);
        Assert.Equal(a.Path, rename.OldPath);
        Assert.EndsWith("030_A.package", rename.NewPath);
    }

    [Fact]
    public void MovingIntoAGapWithRoomRenamesOnlyTheMovedItem()
    {
        var a = Item("A.package", 0);
        var b = Item("B.package", 10);
        var c = Item("C.package", 20);
        var d = Item("D.package", 30);

        // Move D between A and B.
        var renames = LoadOrderReorderPlanner.Plan(new[] { a, b, c, d }, fromIndex: 3, toIndex: 1);

        var rename = Assert.Single(renames);
        Assert.Equal(d.Path, rename.OldPath);
        Assert.EndsWith("005_D.package", rename.NewPath);
    }

    [Fact]
    public void MovingToTheVeryFrontWhenFrontIsAlreadyZeroForcesFullRenumber()
    {
        var a = Item("A.package", 0);
        var b = Item("B.package", 10);
        var c = Item("C.package", 20);

        // Move C to the front: nothing fits before 0 without going negative.
        var renames = LoadOrderReorderPlanner.Plan(new[] { a, b, c }, fromIndex: 2, toIndex: 0);

        Assert.Equal(3, renames.Count);
        Assert.Contains(renames, r => r.OldPath == c.Path && r.NewPath.EndsWith("000_C.package"));
        Assert.Contains(renames, r => r.OldPath == a.Path && r.NewPath.EndsWith("010_A.package"));
        Assert.Contains(renames, r => r.OldPath == b.Path && r.NewPath.EndsWith("020_B.package"));
    }

    [Fact]
    public void NoNumericGapBetweenNeighborsForcesFullRenumber()
    {
        var a = Item("A.package", 0);
        var b = Item("B.package", 1);
        var c = Item("C.package", 2);

        // Move C between A and B - adjacent numbers 0/1 leave no room for a clean integer.
        var renames = LoadOrderReorderPlanner.Plan(new[] { a, b, c }, fromIndex: 2, toIndex: 1);

        Assert.Contains(renames, r => r.OldPath == c.Path && r.NewPath.EndsWith("010_C.package"));
        Assert.Contains(renames, r => r.OldPath == b.Path && r.NewPath.EndsWith("020_B.package"));
        Assert.DoesNotContain(renames, r => r.OldPath == a.Path); // already 000, unaffected
    }

    [Fact]
    public void OutOfOrderOrMissingExistingPrefixesForceFullRenumberEvenWithApparentRoom()
    {
        var a = Item("A.package", 5);
        var b = Item("B.package", 3); // out of order relative to A - nothing reliable to trust
        var c = Item("C.package", null);

        var renames = LoadOrderReorderPlanner.Plan(new[] { a, b, c }, fromIndex: 2, toIndex: 1);

        Assert.Contains(renames, r => r.OldPath == a.Path && r.NewPath.EndsWith("000_A.package"));
        Assert.Contains(renames, r => r.OldPath == c.Path && r.NewPath.EndsWith("010_C.package"));
        Assert.Contains(renames, r => r.OldPath == b.Path && r.NewPath.EndsWith("020_B.package"));
    }

    [Fact]
    public void MovingToItsOwnPositionIsANoOp()
    {
        var a = Item("A.package", 0);
        var b = Item("B.package", 10);

        Assert.Empty(LoadOrderReorderPlanner.Plan(new[] { a, b }, fromIndex: 1, toIndex: 1));
    }
}
