using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="Document"/>.
/// </summary>
public static class DocumentExtensions
{
    /// <summary>
    /// Returns all <see cref="BuiltInCategory"/> values that have a valid
    /// <see cref="Category"/> in <paramref name="document"/>.
    /// Enum values that throw or resolve to <c>null</c> are silently skipped.
    /// </summary>
    public static IList<BuiltInCategory> GetValidCategories(this Document document)
    {
        return Enum.GetValues(typeof(BuiltInCategory))
            .Cast<BuiltInCategory>()
            .Where(bic =>
            {
                try { return Category.GetCategory(document, bic) is not null; }
                catch { return false; }
            })
            .ToList();
    }

    /// <summary>
    /// Returns the <see cref="BuiltInCategory"/> whose display name equals
    /// <paramref name="categoryName"/>, or <see cref="BuiltInCategory.INVALID"/>
    /// when no match is found.
    /// </summary>
    public static BuiltInCategory GetBuiltInCategory(this Document document, string categoryName)
    {
#if REVIT_2024 || REVIT_2023 || REVIT_2022 || REVIT_2021 || REVIT_2020
        var result = document.GetValidCategories()
                    .FirstOrDefault(
                        bic => Category.GetCategory(document, bic).Name == categoryName);

        return result;
#else

        return document.GetValidCategories()
            .FirstOrDefault(
                bic => Category.GetCategory(document, bic).Name == categoryName,
                BuiltInCategory.INVALID);
#endif
    }

    /// <summary>
    /// Returns <c>true</c> when a view named <paramref name="viewName"/> exists in
    /// <paramref name="document"/>.
    /// </summary>
    public static bool ViewExists(this Document document, string viewName)
    {
        var pvp = new ParameterValueProvider(new ElementId(BuiltInParameter.VIEW_NAME));
        var rule = new FilterStringRule(pvp, new FilterStringEquals(), viewName);
        var filter = new ElementParameterFilter(rule);

        return new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Views)
            .WherePasses(filter)
            .FirstOrDefault() is View;
    }
}
