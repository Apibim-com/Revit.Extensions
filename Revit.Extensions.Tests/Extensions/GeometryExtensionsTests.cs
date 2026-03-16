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

    [Fact(Skip = RevitRequired)]
    public void SignedDistanceTo_PointAbovePlane_ReturnsPositiveDistance()
    {
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        XYZ point = new XYZ(0, 0, 5);
        plane.SignedDistanceTo(point).Should().BeApproximately(5.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void SignedDistanceTo_PointBelowPlane_ReturnsNegativeDistance()
    {
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        XYZ point = new XYZ(0, 0, -3);
        plane.SignedDistanceTo(point).Should().BeApproximately(-3.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ProjectOnto_PointAbovePlane_ReturnsFootOfPerpendicular()
    {
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        XYZ point = new XYZ(3, 4, 7);
        XYZ projected = plane.ProjectOnto(point);
        projected.X.Should().BeApproximately(3.0, 1e-9);
        projected.Y.Should().BeApproximately(4.0, 1e-9);
        projected.Z.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ProjectOnto_PointOnPlane_ReturnsSamePoint()
    {
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        XYZ point = new XYZ(1, 2, 0);
        XYZ projected = plane.ProjectOnto(point);
        projected.X.Should().BeApproximately(1.0, 1e-9);
        projected.Y.Should().BeApproximately(2.0, 1e-9);
        projected.Z.Should().BeApproximately(0.0, 1e-9);
    }

    // -------------------------------------------------------------------------
    // GetTransformed
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void GetTransformed_IdentityTransform_ReturnsSameBounds()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(1, 2, 3), Max = new XYZ(4, 5, 6) };

        var result = bbox.GetTransformed(Transform.Identity);

        result.Should().NotBeNull();
        result!.Min.X.Should().BeApproximately(1, 1e-9);
        result.Min.Y.Should().BeApproximately(2, 1e-9);
        result.Min.Z.Should().BeApproximately(3, 1e-9);
        result.Max.X.Should().BeApproximately(4, 1e-9);
        result.Max.Y.Should().BeApproximately(5, 1e-9);
        result.Max.Z.Should().BeApproximately(6, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void GetTransformed_Translation_ShiftsAllCorners()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1) };
        Transform translation = Transform.CreateTranslation(new XYZ(5, 0, 0));

        var result = bbox.GetTransformed(translation);

        result!.Min.X.Should().BeApproximately(5, 1e-9);
        result.Max.X.Should().BeApproximately(6, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void GetTransformed_NullBbox_ReturnsNull()
    {
        BoundingBoxXYZ bbox = null!;
        bbox.GetTransformed(Transform.Identity).Should().BeNull();
    }

    [Fact(Skip = RevitRequired)]
    public void GetTransformed_NullTransform_ReturnsNull()
    {
        var bbox = new BoundingBoxXYZ { Min = XYZ.Zero, Max = new XYZ(1, 1, 1) };
        bbox.GetTransformed(null!).Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Contains (point)
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void Contains_PointInsideBox_ReturnsTrue()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(2, 2, 2) };
        bbox.Contains(new XYZ(1, 1, 1)).Should().BeTrue();
    }

    [Fact(Skip = RevitRequired)]
    public void Contains_PointOnBoundary_ReturnsTrue()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(2, 2, 2) };
        bbox.Contains(new XYZ(0, 0, 0)).Should().BeTrue();
        bbox.Contains(new XYZ(2, 2, 2)).Should().BeTrue();
    }

    [Fact(Skip = RevitRequired)]
    public void Contains_PointOutsideBox_ReturnsFalse()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(2, 2, 2) };
        bbox.Contains(new XYZ(3, 1, 1)).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Contains (box)
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void Contains_InnerBoxFullyInside_ReturnsTrue()
    {
        var outer = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(4, 4, 4) };
        var inner = new BoundingBoxXYZ { Min = new XYZ(1, 1, 1), Max = new XYZ(3, 3, 3) };
        outer.Contains(inner).Should().BeTrue();
    }

    [Fact(Skip = RevitRequired)]
    public void Contains_BoxPartiallyOutside_ReturnsFalse()
    {
        var outer = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(2, 2, 2) };
        var inner = new BoundingBoxXYZ { Min = new XYZ(1, 1, 1), Max = new XYZ(3, 3, 3) };
        outer.Contains(inner).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // ComputeCentroid
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void ComputeCentroid_SymmetricBox_ReturnsCentre()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(4, 6, 8) };
        var c = bbox.ComputeCentroid();
        c.X.Should().BeApproximately(2, 1e-9);
        c.Y.Should().BeApproximately(3, 1e-9);
        c.Z.Should().BeApproximately(4, 1e-9);
    }

    // -------------------------------------------------------------------------
    // ComputeVolume
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void ComputeVolume_UnitBox_Returns1()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1) };
        bbox.ComputeVolume().Should().BeApproximately(1.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ComputeVolume_DegenerateBox_Returns0()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(0, 1, 1) };
        bbox.ComputeVolume().Should().Be(0);
    }

    [Fact(Skip = RevitRequired)]
    public void ComputeVolume_ArbitraryBox_ReturnsCorrectVolume()
    {
        var bbox = new BoundingBoxXYZ { Min = new XYZ(1, 2, 3), Max = new XYZ(4, 6, 8) };
        bbox.ComputeVolume().Should().BeApproximately(60.0, 1e-9);
    }

    // -------------------------------------------------------------------------
    // Intersects (with tolerance)
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void Intersects_WithTolerance_BoxesJustOutOfRangeButWithinTolerance_ReturnsTrue()
    {
        var a = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1) };
        var b = new BoundingBoxXYZ { Min = new XYZ(1.5, 0, 0), Max = new XYZ(3, 1, 1) };
        a.Intersects(b, tolerance: 1.0).Should().BeTrue();
        a.Intersects(b, tolerance: 0.1).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Combine
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void Combine_TwoBoxes_ReturnsEnclosingBox()
    {
        var a = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1) };
        var b = new BoundingBoxXYZ { Min = new XYZ(-1, 2, 0), Max = new XYZ(3, 3, 5) };
        var result = a.Combine(b);
        result.Min.X.Should().BeApproximately(-1, 1e-9);
        result.Min.Y.Should().BeApproximately(0, 1e-9);
        result.Min.Z.Should().BeApproximately(0, 1e-9);
        result.Max.X.Should().BeApproximately(3, 1e-9);
        result.Max.Y.Should().BeApproximately(3, 1e-9);
        result.Max.Z.Should().BeApproximately(5, 1e-9);
    }

    // -------------------------------------------------------------------------
    // Intersects
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void Intersects_OverlappingBoxes_ReturnsTrue()
    {
        var a = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(2, 2, 2) };
        var b = new BoundingBoxXYZ { Min = new XYZ(1, 1, 1), Max = new XYZ(3, 3, 3) };
        a.Intersects(b).Should().BeTrue();
    }

    [Fact(Skip = RevitRequired)]
    public void Intersects_NonOverlappingBoxes_ReturnsFalse()
    {
        var a = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1) };
        var b = new BoundingBoxXYZ { Min = new XYZ(2, 2, 2), Max = new XYZ(3, 3, 3) };
        a.Intersects(b).Should().BeFalse();
    }

    [Fact(Skip = RevitRequired)]
    public void Intersects_TouchingFaces_ReturnsTrue()
    {
        var a = new BoundingBoxXYZ { Min = new XYZ(0, 0, 0), Max = new XYZ(1, 1, 1) };
        var b = new BoundingBoxXYZ { Min = new XYZ(1, 0, 0), Max = new XYZ(2, 1, 1) };
        a.Intersects(b).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // GetPathLength
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void GetPathLength_TwoPoints_ReturnsDistance()
    {
        var points = new[] { new XYZ(0, 0, 0), new XYZ(3, 4, 0) };
        points.GetPathLength().Should().BeApproximately(5.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void GetPathLength_ThreePoints_ReturnsSumOfSegments()
    {
        var points = new[] { new XYZ(0, 0, 0), new XYZ(1, 0, 0), new XYZ(1, 1, 0) };
        points.GetPathLength().Should().BeApproximately(2.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void GetPathLength_SinglePoint_ReturnsZero()
    {
        var points = new[] { new XYZ(1, 2, 3) };
        points.GetPathLength().Should().Be(0.0);
    }

    // -------------------------------------------------------------------------
    // ProjectOnto (Curve)
    // -------------------------------------------------------------------------

    [Fact(Skip = RevitRequired)]
    public void ProjectOnto_Line_ProjectsEndpointsOntoXyPlane()
    {
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        Curve line = Line.CreateBound(new XYZ(0, 0, 5), new XYZ(1, 0, 5));

        Curve result = line.ProjectOnto(plane);

        result.Should().BeOfType<Line>();
        result.GetEndPoint(0).Z.Should().BeApproximately(0, 1e-9);
        result.GetEndPoint(1).Z.Should().BeApproximately(0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ProjectOnto_LineOnPlane_ReturnsSameLine()
    {
        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        Curve line = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(1, 0, 0));

        Curve result = line.ProjectOnto(plane);

        result.GetEndPoint(0).Z.Should().BeApproximately(0, 1e-9);
        result.GetEndPoint(1).Z.Should().BeApproximately(0, 1e-9);
    }
}
