using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

public class DoubleExtensionsTests
{
    // -------------------------------------------------------------------------
    // IsAlmostEqual
    // -------------------------------------------------------------------------

    [Fact]
    public void IsAlmostEqual_EqualValues_ReturnsTrue()
    {
        1.0.IsAlmostEqual(1.0).Should().BeTrue();
    }

    [Fact]
    public void IsAlmostEqual_DifferenceWithinDefaultTolerance_ReturnsTrue()
    {
        1.0.IsAlmostEqual(1.0 + 1e-10).Should().BeTrue();
    }

    [Fact]
    public void IsAlmostEqual_DifferenceExceedsDefaultTolerance_ReturnsFalse()
    {
        1.0.IsAlmostEqual(1.0 + 1e-8).Should().BeFalse();
    }

    [Fact]
    public void IsAlmostEqual_CustomTolerance_UsesProvidedTolerance()
    {
        1.0.IsAlmostEqual(1.05, tolerance: 0.1).Should().BeTrue();
        1.0.IsAlmostEqual(1.2, tolerance: 0.1).Should().BeFalse();
    }

    [Fact]
    public void IsAlmostEqual_NegativeValues_WorksCorrectly()
    {
        (-3.0).IsAlmostEqual(-3.0 + 1e-10).Should().BeTrue();
        (-3.0).IsAlmostEqual(-3.0 + 1e-8).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Round
    // -------------------------------------------------------------------------

    [Fact]
    public void Round_DefaultDigits_RoundsToNineDecimalPlaces()
    {
        double value = 1.1234567891234;
        value.Round().Should().Be(Math.Round(value, 9));
    }

    [Fact]
    public void Round_CustomDigits_RoundsToGivenPlaces()
    {
        1.23456789.Round(3).Should().BeApproximately(1.235, 1e-9);
    }

    [Fact]
    public void Round_ZeroDigits_ReturnsInteger()
    {
        1.7.Round(0).Should().Be(2.0);
    }
}
