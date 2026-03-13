namespace Revit.Extensions;

/// <summary>
/// Extension methods for unit conversions that do not require the Revit API.
/// </summary>
public static class UnitExtensions
{
    private const double MmPerInch = 25.4;
    private const double MmPerMeter = 1000.0;

    public static double MillimetersToInches(this double mm) => mm / MmPerInch;
    public static double InchesToMillimeters(this double inches) => inches * MmPerInch;

    public static double MetersToInches(this double meters) => meters * MmPerMeter / MmPerInch;
    public static double InchesToMeters(this double inches) => inches * MmPerInch / MmPerMeter;
}
