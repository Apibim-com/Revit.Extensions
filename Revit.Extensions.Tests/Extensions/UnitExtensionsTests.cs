using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

public class UnitExtensionsTests
{
    [Fact]
    public void MillimetersToInches_25_4mm_Returns1Inch() =>
        (25.4).MillimetersToInches().Should().BeApproximately(1.0, 1e-9);

    [Fact]
    public void InchesToMillimeters_1Inch_Returns25_4mm() =>
        (1.0).InchesToMillimeters().Should().BeApproximately(25.4, 1e-9);

    [Fact]
    public void MetersToInches_1m_Returns39_3701Inches() =>
        (1.0).MetersToInches().Should().BeApproximately(39.37007874, 1e-6);

    [Fact]
    public void InchesToMeters_39_3701Inches_Returns1m() =>
        (39.37007874015748).InchesToMeters().Should().BeApproximately(1.0, 1e-9);
}
