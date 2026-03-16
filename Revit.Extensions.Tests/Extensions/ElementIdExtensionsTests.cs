using Autodesk.Revit.DB;
using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

/// <summary>
/// Tests for <see cref="ElementIdExtensions"/>.
/// All tests require RevitAPI.dll at runtime and are skipped in headless CI.
/// </summary>
public class ElementIdExtensionsTests
{
    private const string RevitRequired = "Requires RevitAPI.dll at runtime (Revit installation).";

    // -------------------------------------------------------------------------
    // IsValid / IsInvalid
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void IsValid_InvalidElementId_ReturnsFalse() =>
        ElementId.InvalidElementId.IsValid().Should().BeFalse();

    [Fact(Skip = RevitRequired)]
    public void IsValid_NullElementId_ReturnsFalse() =>
        ((ElementId)null!).IsValid().Should().BeFalse();

    [Fact(Skip = RevitRequired)]
    public void IsValid_PositiveId_ReturnsTrue() =>
        new ElementId(1).IsValid().Should().BeTrue();

    [Fact(Skip = RevitRequired)]
    public void IsInvalid_InvalidElementId_ReturnsTrue() =>
        ElementId.InvalidElementId.IsInvalid().Should().BeTrue();

    // -------------------------------------------------------------------------
    // IsUserCreated / IsBuiltIn
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void IsUserCreated_PositiveId_ReturnsTrue() =>
        new ElementId(42).IsUserCreated().Should().BeTrue();

    [Fact(Skip = RevitRequired)]
    public void IsUserCreated_InvalidElementId_ReturnsFalse() =>
        ElementId.InvalidElementId.IsUserCreated().Should().BeFalse();

    [Fact(Skip = RevitRequired)]
    public void IsBuiltIn_WallCategory_ReturnsTrue() =>
        new ElementId(BuiltInCategory.OST_Walls).IsBuiltIn().Should().BeTrue();

    [Fact(Skip = RevitRequired)]
    public void IsBuiltIn_PositiveId_ReturnsFalse() =>
        new ElementId(1).IsBuiltIn().Should().BeFalse();

    // -------------------------------------------------------------------------
    // ToBuiltInCategory / ToBuiltInParameter
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void ToBuiltInCategory_WallsCategoryId_ReturnsOstWalls()
    {
        var id = new ElementId(BuiltInCategory.OST_Walls);
        id.ToBuiltInCategory().Should().Be(BuiltInCategory.OST_Walls);
    }

    [Fact(Skip = RevitRequired)]
    public void ToBuiltInParameter_CommentsParamId_ReturnsAllModelInstanceComments()
    {
        var id = new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        id.ToBuiltInParameter().Should().Be(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
    }

    // -------------------------------------------------------------------------
    // ToElementId (factory direction)
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void ToElementId_BuiltInCategory_RoundTrips()
    {
        const BuiltInCategory bic = BuiltInCategory.OST_Doors;
        bic.ToElementId().ToBuiltInCategory().Should().Be(bic);
    }

    [Fact(Skip = RevitRequired)]
    public void ToElementId_BuiltInParameter_RoundTrips()
    {
        const BuiltInParameter bip = BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM;
        bip.ToElementId().ToBuiltInParameter().Should().Be(bip);
    }

    // -------------------------------------------------------------------------
    // GetElement<T> / TryGetElement<T>
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void GetElement_NullId_ThrowsArgumentNullException()
    {
        ElementId id = null!;
        var act = () => id.GetElement<Element>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact(Skip = RevitRequired)]
    public void TryGetElement_InvalidId_ReturnsFalse()
    {
        // Requires a real Document — wire up in Revit Test Runner.
        // Document doc = ...;
        // ElementId.InvalidElementId.TryGetElement<Wall>(doc, out _).Should().BeFalse();
        throw new NotImplementedException("Wire up with a real Revit document.");
    }
}
