using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for Revit family editing API.
/// </summary>
public static class FamilyExtensions
{
    /// <summary>
    /// Returns all <see cref="FamilyParameter"/> objects managed by
    /// <paramref name="familyManager"/>.
    /// Returns an empty list when <paramref name="familyManager"/> is <c>null</c>.
    /// </summary>
    public static List<FamilyParameter> GetFamilyParameters(this FamilyManager familyManager)
    {
        if (familyManager is null)
            return [];

        return familyManager.Parameters
            .OfType<FamilyParameter>()
            .ToList();
    }
}
