using Autodesk.Revit.DB;

namespace Apibim.Revit.Extensions;

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
}
