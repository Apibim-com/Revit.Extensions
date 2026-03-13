using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for building <see cref="ElementFilter"/> objects.
/// </summary>
public static class FilterExtensions
{
    /// <summary>
    /// Returns a filter that matches element <em>instances</em> of
    /// <paramref name="category"/> (excludes element types).
    /// </summary>
    /// <example>
    /// <code>
    /// var filter = BuiltInCategory.OST_Walls.ToInstanceFilter();
    /// new FilteredElementCollector(doc).WherePasses(filter).ToElements();
    /// </code>
    /// </example>
    public static ElementFilter ToInstanceFilter(this BuiltInCategory category) =>
        new LogicalAndFilter(
            new ElementIsElementTypeFilter(inverted: true),
            new ElementCategoryFilter(category));

    /// <summary>
    /// Returns a filter that matches element <em>types</em> of
    /// <paramref name="category"/> (excludes instances).
    /// </summary>
    /// <example>
    /// <code>
    /// var filter = BuiltInCategory.OST_Walls.ToTypeFilter();
    /// new FilteredElementCollector(doc).WherePasses(filter).ToElements();
    /// </code>
    /// </example>
    public static ElementFilter ToTypeFilter(this BuiltInCategory category) =>
        new LogicalAndFilter(
            new ElementIsElementTypeFilter(inverted: false),
            new ElementCategoryFilter(category));
}
