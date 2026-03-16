using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Fluent extension methods for transforming Revit elements (move, rotate, mirror, copy).
/// Wraps <see cref="ElementTransformUtils"/> and returns the element (or new element) for chaining.
/// All methods must be called inside an open <see cref="Transaction"/>.
/// </summary>
public static class ElementTransformExtensions
{
    /// <summary>
    /// Moves <paramref name="element"/> by the given offsets and returns it for chaining.
    /// </summary>
    public static Element Move(this Element element, double x, double y, double z)
    {
        ElementTransformUtils.MoveElement(element.Document, element.Id, new XYZ(x, y, z));
        return element;
    }

    /// <summary>
    /// Moves <paramref name="element"/> by <paramref name="translation"/> and returns it for chaining.
    /// </summary>
    public static Element Move(this Element element, XYZ translation)
    {
        ElementTransformUtils.MoveElement(element.Document, element.Id, translation);
        return element;
    }

    /// <summary>
    /// Rotates <paramref name="element"/> around <paramref name="axis"/> by
    /// <paramref name="angle"/> radians and returns it for chaining.
    /// </summary>
    public static Element Rotate(this Element element, Line axis, double angle)
    {
        ElementTransformUtils.RotateElement(element.Document, element.Id, axis, angle);
        return element;
    }

    /// <summary>
    /// Mirrors <paramref name="element"/> about <paramref name="plane"/> in place and returns it.
    /// Returns <c>null</c> when the element cannot be mirrored.
    /// </summary>
    public static Element? Mirror(this Element element, Plane plane)
    {
        if (!ElementTransformUtils.CanMirrorElement(element.Document, element.Id))
            return null;

        ElementTransformUtils.MirrorElement(element.Document, element.Id, plane);
        return element;
    }

    /// <summary>
    /// Creates a copy of <paramref name="element"/> offset by <paramref name="translation"/>
    /// and returns the new element.
    /// Returns <c>null</c> when the copy operation produces no result.
    /// </summary>
    public static Element? Copy(this Element element, XYZ translation)
    {
        ICollection<ElementId> ids = ElementTransformUtils.CopyElement(
            element.Document, element.Id, translation);

        ElementId? newId = ids.FirstOrDefault();
        return newId is null ? null : element.Document.GetElement(newId);
    }

    /// <summary>
    /// Creates a copy of <paramref name="element"/> offset by the given values
    /// and returns the new element.
    /// Returns <c>null</c> when the copy operation produces no result.
    /// </summary>
    public static Element? Copy(this Element element, double x, double y, double z) =>
        element.Copy(new XYZ(x, y, z));
}
