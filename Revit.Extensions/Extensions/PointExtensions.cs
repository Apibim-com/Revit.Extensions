using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for working with Revit coordinate points.
/// </summary>
public static class PointExtensions
{
#if REVIT_2020
    /// <summary>
    /// Converts an <see cref="XYZ"/> point from Revit internal units to the specified
    /// <see cref="DisplayUnitType"/> (Revit 2020 API).
    /// </summary>
    public static XYZ Recalculate(this XYZ pointToChange, DisplayUnitType forgeTypeId)
    {
        double x = UnitUtils.ConvertFromInternalUnits(pointToChange.X, forgeTypeId);
        double y = UnitUtils.ConvertFromInternalUnits(pointToChange.Y, forgeTypeId);
        double z = UnitUtils.ConvertFromInternalUnits(pointToChange.Z, forgeTypeId);
        return new(x, y, z);
    }
#else
    /// <summary>
    /// Converts an <see cref="XYZ"/> point from Revit internal units to the specified
    /// <see cref="ForgeTypeId"/> unit type (Revit 2021+).
    /// </summary>
    public static XYZ Recalculate(this XYZ pointToChange, ForgeTypeId forgeTypeId)
    {
        double x = UnitUtils.ConvertFromInternalUnits(pointToChange.X, forgeTypeId);
        double y = UnitUtils.ConvertFromInternalUnits(pointToChange.Y, forgeTypeId);
        double z = UnitUtils.ConvertFromInternalUnits(pointToChange.Z, forgeTypeId);
        return new(x, y, z);
    }
#endif

    /// <summary>
    /// Converts an <see cref="XYZ"/> point from Revit internal units (feet) to millimeters
    /// without requiring <c>UnitUtils</c> — works headless.
    /// </summary>
    public static XYZ ToMillimeters(this XYZ point) =>
        new XYZ(
            point.X.FeetToMillimeters(),
            point.Y.FeetToMillimeters(),
            point.Z.FeetToMillimeters());

    /// <summary>
    /// Converts an <see cref="XYZ"/> point from millimeters to Revit internal units (feet)
    /// without requiring <c>UnitUtils</c> — works headless.
    /// </summary>
    public static XYZ FromMillimeters(this XYZ point) =>
        new XYZ(
            point.X.MillimetersToFeet(),
            point.Y.MillimetersToFeet(),
            point.Z.MillimetersToFeet());
}
