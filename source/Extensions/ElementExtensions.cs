using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Apibim.Revit.Extensions;

/// <summary>
/// Extension methods for working with Revit elements and collectors.
/// </summary>
public static class ElementExtensions
{
    /// <summary>
    /// Returns all selected elements, or all visible family instances and host objects
    /// on the active view when fewer than 2 elements are selected.
    /// </summary>
    public static IList<Element> GetAllElementOrSelected(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        ICollection<ElementId> elementIds = uiDocument.Selection.GetElementIds();

        FilteredElementCollector collector = elementIds.Count < 2
            ? new(document, uiDocument.ActiveView.Id)
            : new(document, elementIds);

        var elements1 = collector
            .OfClass(typeof(FamilyInstance))
            .ToElements()
            .OfType<FamilyInstance>()
            .Where(f => f.SuperComponent is null)
            .ToList();

        collector = elementIds.Count < 2
            ? new(document, uiDocument.ActiveView.Id)
            : new(document, elementIds);

        var elements2 = collector
            .OfClass(typeof(HostObject))
            .ToElements();

        return elements1.Concat(elements2).ToList();
    }

    /// <summary>
    /// Collects all grids visible on the view of the level closest to elevation zero.
    /// Falls back to all grids in the document if no level with 2+ grids is found.
    /// </summary>
    public static IList<Grid> GetAllGridFromFirstLevel(Document document, out string? firstLevelName)
    {
        var levels = new FilteredElementCollector(document, document.ActiveView.Id)
            .OfClass(typeof(Level))
            .OfType<Level>()
            .OrderBy(l => Math.Abs(l.Elevation));

        if (!levels.Any())
            levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .OrderBy(l => Math.Abs(l.Elevation));

        foreach (var level in levels)
        {
            ElementLevelFilter levelFilter = new(level.Id);

            var grids = new FilteredElementCollector(document, document.ActiveView.Id)
                .OfClass(typeof(Grid))
                .WherePasses(levelFilter)
                .OfType<Grid>()
                .ToList();

            if (grids.Count < 2)
                continue;

            firstLevelName = level.Name;
            return grids;
        }

        firstLevelName = null;

        var allGrids = new FilteredElementCollector(document, document.ActiveView.Id)
            .OfClass(typeof(Grid))
            .OfType<Grid>()
            .ToList();

        if (allGrids.Count == 0)
            allGrids = new FilteredElementCollector(document)
                .OfClass(typeof(Grid))
                .OfType<Grid>()
                .ToList();

        return allGrids;
    }
}
