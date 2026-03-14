using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for creating <see cref="DirectShape"/> elements.
/// </summary>
public static class DirectShapeExtensions
{
    private const BuiltInCategory DefaultCategory = BuiltInCategory.OST_GenericModel;

    /// <summary>
    /// Creates a <see cref="DirectShape"/> containing <paramref name="geometryObjects"/>
    /// in the given <paramref name="category"/>.
    /// </summary>
    public static DirectShape CreateDirectShape(
        this Document document,
        IEnumerable<GeometryObject> geometryObjects,
        BuiltInCategory category = DefaultCategory)
    {
        var shape = DirectShape.CreateElement(document, new ElementId(category));
        shape.SetName(shape.Category.Name);
        shape.SetShape(geometryObjects.ToList());
        return shape;
    }

    /// <summary>
    /// Creates a <see cref="DirectShape"/> containing a single
    /// <paramref name="geometryObject"/>.
    /// </summary>
    public static DirectShape CreateDirectShape(
        this Document document,
        GeometryObject geometryObject,
        BuiltInCategory category = DefaultCategory) =>
        document.CreateDirectShape([geometryObject], category);

    /// <summary>
    /// Creates a <see cref="DirectShape"/> visualising the line segment between
    /// <paramref name="point1"/> and <paramref name="point2"/>.
    /// Useful for debug visualisation of edges and path segments.
    /// </summary>
    public static DirectShape CreateDirectShape(
        this Document document,
        XYZ point1,
        XYZ point2,
        BuiltInCategory category = DefaultCategory) =>
        document.CreateDirectShape(Line.CreateBound(point1, point2), category);
}
