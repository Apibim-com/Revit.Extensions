using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="View"/>.
/// </summary>
public static class ViewExtensions
{
    /// <summary>
    /// Returns the <see cref="Viewport"/> that hosts this view on a sheet,
    /// or <c>null</c> when the view is not placed on any sheet.
    /// </summary>
    /// <summary>
    /// Returns the scope box (<c>Volume of Interest</c>) assigned to <paramref name="view"/>,
    /// or <c>null</c> when none is assigned.
    /// </summary>
    public static Element? GetScopeBox(this View view)
    {
        Parameter? p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);

        if (p is null || !p.HasValue)
            return null;

        ElementId id = p.AsElementId();
        return id.IsValid() ? view.Document.GetElement(id) : null;
    }

    public static Viewport? GetViewport(this View view)
    {
        var status = view.GetPlacementOnSheetStatus();
        if (status == ViewPlacementOnSheetStatus.NotApplicable ||
            status == ViewPlacementOnSheetStatus.NotPlaced)
            return null;

        var pvp    = new ParameterValueProvider(new ElementId(BuiltInParameter.VIEWPORT_VIEW));
        var rule   = new FilterElementIdRule(pvp, new FilterNumericEquals(), view.Id);
        var filter = new ElementParameterFilter(rule);

        return new FilteredElementCollector(view.Document)
            .OfCategory(BuiltInCategory.OST_Viewports)
            .WherePasses(filter)
            .FirstElement() as Viewport;
    }
}
