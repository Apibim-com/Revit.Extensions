namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="double"/> values, tuned to Revit's numeric precision.
/// </summary>
public static class DoubleExtensions
{
    private const double DefaultTolerance = 1e-9;

    /// <summary>
    /// Returns <c>true</c> when the absolute difference between <paramref name="value"/>
    /// and <paramref name="other"/> is within <paramref name="tolerance"/>.
    /// Default tolerance (1e-9) matches Revit's internal precision.
    /// </summary>
    public static bool IsAlmostEqual(this double value, double other, double tolerance = DefaultTolerance) =>
        Math.Abs(value - other) <= tolerance;

    /// <summary>
    /// Rounds <paramref name="value"/> to <paramref name="digits"/> decimal places.
    /// Defaults to 9 digits, matching Revit's internal precision.
    /// </summary>
    public static double Round(this double value, int digits = 9) =>
        Math.Round(value, digits);
}
