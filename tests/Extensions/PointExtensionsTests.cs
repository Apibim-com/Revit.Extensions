using Autodesk.Revit.DB;
using FluentAssertions;
using Xunit;

namespace Apibim.Revit.Extensions.Tests;

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
}
