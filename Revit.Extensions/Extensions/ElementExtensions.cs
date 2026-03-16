using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit.Extensions;

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
    /// Returns the world-space bounding box of <paramref name="element"/> as an
    /// <see cref="Outline"/> expanded by <paramref name="enlargement"/> on every side.
    /// The default enlargement is 1 mm in feet (<c>0.00328084</c>).
    /// Returns <c>null</c> when the element has no bounding box.
    /// </summary>
    public static Outline? GetEnlargedOutline(this Element element, double enlargement = OneMmInFt)
    {
        try
        {
            BoundingBoxXYZ? bbox = element.get_BoundingBox(null);
            if (bbox is null)
                return null;

            double minX = Math.Min(bbox.Min.X, bbox.Max.X);
            double minY = Math.Min(bbox.Min.Y, bbox.Max.Y);
            double minZ = Math.Min(bbox.Min.Z, bbox.Max.Z);
            double maxX = Math.Max(bbox.Min.X, bbox.Max.X);
            double maxY = Math.Max(bbox.Min.Y, bbox.Max.Y);
            double maxZ = Math.Max(bbox.Min.Z, bbox.Max.Z);

            return new Outline(
                new XYZ(minX - enlargement, minY - enlargement, minZ - enlargement),
                new XYZ(maxX + enlargement, maxY + enlargement, maxZ + enlargement));
        }
        catch
        {
            return null;
        }
    }

    private const double OneMmInFt = 0.00328084;
}
