using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for working with Revit geometry types.
/// </summary>
public static class GeometryExtensions
{
    /// <summary>
    /// Converts an <see cref="XYZ"/> point to a value tuple <c>(X, Y, Z)</c>.
    /// </summary>
    public static (double X, double Y, double Z) ToVector(this XYZ point)
    {
        return (point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Converts a value tuple <c>(X, Y, Z)</c> to an <see cref="XYZ"/> point.
    /// </summary>
    public static XYZ ToXYZ(this (double X, double Y, double Z) point)
    {
        return new XYZ(point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Expands an <see cref="Outline"/> uniformly by <paramref name="size"/> in all directions.
    /// </summary>
    public static Outline Extend(this Outline outline, double size = 10)
    {
        XYZ offsetPoint = new(size, size, size);
        XYZ minPoint = outline.MinimumPoint - offsetPoint;
        XYZ maxPoint = outline.MaximumPoint + offsetPoint;
        return new Outline(minPoint, maxPoint);
    }

    /// <summary>
    /// Applies the bounding box's own <see cref="BoundingBoxXYZ.Transform"/> to its
    /// <see cref="BoundingBoxXYZ.Min"/> and <see cref="BoundingBoxXYZ.Max"/> corners,
    /// returning a new world-space bounding box.
    /// </summary>
    public static BoundingBoxXYZ? TransformBoundingBox(this BoundingBoxXYZ bbox)
    {
        if (bbox is null || bbox.Min is null || bbox.Max is null)
            return null;

        Transform transform = bbox.Transform;

        XYZ transformedMin = transform.OfPoint(bbox.Min);
        XYZ transformedMax = transform.OfPoint(bbox.Max);

        return new BoundingBoxXYZ
        {
            Min = transformedMin,
            Max = transformedMax,
            Transform = transform.Multiply(bbox.Transform)
        };
    }
}
