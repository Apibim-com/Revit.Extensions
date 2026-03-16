using Autodesk.Revit.DB;
using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

/// <summary>
/// Tests for <see cref="PointExtensions"/>.
/// NOTE: <c>UnitUtils.ConvertFromInternalUnits</c> requires a running Revit host process.
/// These tests are skipped in headless CI. Run them inside Revit via the Revit Test Runner.
/// </summary>
public class PointExtensionsTests
{
#if !REVIT_2020
    [Fact(Skip = "Requires a running Revit process to initialise UnitUtils.")]
    public void Recalculate_ConvertsFeetToMeters()
    {
        // 1 foot (Revit internal unit) ≈ 0.3048 m
        var point = new XYZ(1.0, 0.0, 0.0);

        var result = point.Recalculate(UnitTypeId.Meters);

        result.X.Should().BeApproximately(0.3048, 1e-4);
        result.Y.Should().BeApproximately(0.0, 1e-9);
        result.Z.Should().BeApproximately(0.0, 1e-9);
    }
#endif

    private const string RevitRequired = "Requires RevitAPI.dll at runtime (Revit installation).";

    [Fact(Skip = RevitRequired)]
    public void ToMillimeters_1FootPoint_Returns304_8mm()
    {
        var point = new XYZ(1.0, 0.0, 0.0);

        var result = point.ToMillimeters();

        result.X.Should().BeApproximately(304.8, 1e-9);
        result.Y.Should().BeApproximately(0.0,   1e-9);
        result.Z.Should().BeApproximately(0.0,   1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ToMillimeters_AllAxes_ConvertsEachComponent()
    {
        var point = new XYZ(1.0, 2.0, 0.5);

        var result = point.ToMillimeters();

        result.X.Should().BeApproximately(304.8,  1e-9);
        result.Y.Should().BeApproximately(609.6,  1e-9);
        result.Z.Should().BeApproximately(152.4,  1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void FromMillimeters_304_8mm_Returns1Foot()
    {
        var point = new XYZ(304.8, 0.0, 0.0);

        var result = point.FromMillimeters();

        result.X.Should().BeApproximately(1.0, 1e-9);
        result.Y.Should().BeApproximately(0.0, 1e-9);
        result.Z.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact(Skip = RevitRequired)]
    public void ToMillimeters_ThenFromMillimeters_RoundTrips()
    {
        var original = new XYZ(3.5, 1.2, 0.75);

        var result = original.ToMillimeters().FromMillimeters();

        result.X.Should().BeApproximately(original.X, 1e-9);
        result.Y.Should().BeApproximately(original.Y, 1e-9);
        result.Z.Should().BeApproximately(original.Z, 1e-9);
    }
}
