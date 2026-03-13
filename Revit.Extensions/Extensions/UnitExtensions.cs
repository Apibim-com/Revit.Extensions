using System;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for unit conversions that do not require the Revit API.
/// </summary>
public static class UnitExtensions
{
    /// <summary>Millimeters per foot (304.8 = 12 inches × 25.4 mm/inch).</summary>
    private const double MmPerFoot = 304.8;

    /// <summary>Converts Revit internal units (feet) to millimeters.</summary>
    public static double FeetToMillimeters(this double feet) => feet * MmPerFoot;

    /// <summary>Converts millimeters to Revit internal units (feet).</summary>
    public static double MillimetersToFeet(this double mm) => mm / MmPerFoot;

    private const double DefaultTolerance = 1e-9;

    public static bool IsZero(this double a, double tolerance = DefaultTolerance) =>
        Math.Abs(a) < tolerance;

    public static bool IsEqual(this double a, double b, double tolerance = DefaultTolerance) =>
        Math.Abs(a - b) < tolerance;
}
