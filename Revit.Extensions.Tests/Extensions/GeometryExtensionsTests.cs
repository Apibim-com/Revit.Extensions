using Autodesk.Revit.DB;
using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

/// <summary>
/// Tests for <see cref="GeometryExtensions"/>.
/// NOTE: These tests use Revit API types (XYZ, Outline, BoundingBoxXYZ) that require
/// RevitAPI.dll at runtime. They are skipped in headless CI and run via Revit Test Runner.
/// </summary>
public class GeometryExtensionsTests
{
    private const string RevitRequired = "Requires RevitAPI.dll at runtime (Revit installation).";

    [Fact(Skip = RevitRequired)]
    public void Extend_DefaultSize_ExpandsOutlineBy10()
    {
        var outline = new Outline(new XYZ(0, 0, 0), new XYZ(1, 1, 1));

        var result = outline.Extend();

        result.MinimumPoint.X.Should().BeApproximately(-10, 1e-9);
        result.MinimumPoint.Y.Should().BeApproximately(-10, 1e-9);
        result.MinimumPoint.Z.Should().BeApproximately(-10, 1e-9);

        result.MaximumPoint.X.Should().BeApproximately(11, 1e-9);
        result.MaximumPoint.Y.Should().BeApproximately(11, 1e-9);
        result.MaximumPoint.Z.Should().BeApproximately(11, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void Extend_CustomSize_ExpandsOutlineByGivenAmount()
    {
        var outline = new Outline(new XYZ(0, 0, 0), new XYZ(1, 1, 1));

        var result = outline.Extend(2);

        result.MinimumPoint.X.Should().BeApproximately(-2, 1e-9);
        result.MinimumPoint.Y.Should().BeApproximately(-2, 1e-9);
        result.MinimumPoint.Z.Should().BeApproximately(-2, 1e-9);

        result.MaximumPoint.X.Should().BeApproximately(3, 1e-9);
        result.MaximumPoint.Y.Should().BeApproximately(3, 1e-9);
        result.MaximumPoint.Z.Should().BeApproximately(3, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void TransformBoundingBox_IdentityTransform_ReturnsUnchangedCorners()
    {
        var bbox = new BoundingBoxXYZ
        {
            Min = new XYZ(0, 0, 0),
            Max = new XYZ(1, 1, 1),
            Transform = Transform.Identity
        };

        var result = bbox.TransformBoundingBox();

        result.Should().NotBeNull();
        result!.Min.X.Should().BeApproximately(0, 1e-9);
        result.Min.Y.Should().BeApproximately(0, 1e-9);
        result.Min.Z.Should().BeApproximately(0, 1e-9);
        result.Max.X.Should().BeApproximately(1, 1e-9);
        result.Max.Y.Should().BeApproximately(1, 1e-9);
        result.Max.Z.Should().BeApproximately(1, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void TransformBoundingBox_NullBbox_ReturnsNull()
    {
        BoundingBoxXYZ bbox = null!;

        var result = bbox.TransformBoundingBox();

        result.Should().BeNull();
    }

    [Fact(Skip = RevitRequired)]
    public void ToVector_ConvertsXyzToTuple()
    {
        var point = new XYZ(1.0, 2.0, 3.0);

        var result = point.ToVector();

        result.X.Should().BeApproximately(1.0, 1e-9);
        result.Y.Should().BeApproximately(2.0, 1e-9);
        result.Z.Should().BeApproximately(3.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ToXYZ_ConvertsTupleToXyz()
    {
        var tuple = (X: 1.0, Y: 2.0, Z: 3.0);

        var result = tuple.ToXYZ();

        result.X.Should().BeApproximately(1.0, 1e-9);
        result.Y.Should().BeApproximately(2.0, 1e-9);
        result.Z.Should().BeApproximately(3.0, 1e-9);
    }
}
