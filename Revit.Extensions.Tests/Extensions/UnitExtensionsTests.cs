using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

public class UnitExtensionsTests
{
    [Fact]
    public void FeetToMillimeters_1Foot_Returns304_8mm() =>
        (1.0).FeetToMillimeters().Should().BeApproximately(304.8, 1e-9);

    [Fact]
    public void FeetToMillimeters_0_ReturnsZero() =>
        (0.0).FeetToMillimeters().Should().Be(0.0);

    [Fact]
    public void MillimetersToFeet_304_8mm_Returns1Foot() =>
        (304.8).MillimetersToFeet().Should().BeApproximately(1.0, 1e-9);

    [Fact]
    public void FeetToMillimeters_ThenBack_RoundTrips() =>
        (3.5).FeetToMillimeters().MillimetersToFeet().Should().BeApproximately(3.5, 1e-9);

    [Fact] public void IsZero_ZeroValue_ReturnsTrue() =>
        (0.0).IsZero().Should().BeTrue();

    [Fact] public void IsZero_SmallValue_ReturnsFalse() =>
        (1e-8).IsZero().Should().BeFalse();

    [Fact] public void IsZero_WithTolerance_ReturnsTrue() =>
        (1e-8).IsZero(1e-7).Should().BeTrue();

    [Fact] public void IsEqual_EqualValues_ReturnsTrue() =>
        (1.0).IsEqual(1.0).Should().BeTrue();

    [Fact] public void IsEqual_DifferentValues_ReturnsFalse() =>
        (1.0).IsEqual(2.0).Should().BeFalse();
}
