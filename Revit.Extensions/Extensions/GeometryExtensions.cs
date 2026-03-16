using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for working with Revit geometry types.
/// </summary>
public static class GeometryExtensions
{
    /// <summary>
    /// Converts an <see cref="XYZ"/> point to a value tuple <c>(X, Y, Z)</c>.
    /// Use this to pass coordinate data across a Revit API boundary — the returned tuple
    /// carries no dependency on <c>RevitAPI.dll</c>, so it can be consumed by business logic,
    /// serialisation code, or unit tests that run without a live Revit process.
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

    /// <summary>
    /// Returns the midpoint between <paramref name="point"/> and <paramref name="other"/>.
    /// </summary>
    public static XYZ GetMidpoint(this XYZ point, XYZ other) => (point + other) / 2;

    /// <summary>
    /// Builds a list of <see cref="Line"/> segments connecting consecutive
    /// <paramref name="points"/>. When <paramref name="isClosed"/> is <c>true</c>
    /// (default) the last point is connected back to the first, forming a closed polygon.
    /// </summary>
    public static IReadOnlyList<Line> GetPolygonLines(
        this IEnumerable<XYZ> points,
        bool isClosed = true)
    {
        IList<XYZ> pts = points as IList<XYZ> ?? points.ToList();
        int segmentCount = isClosed ? pts.Count : pts.Count - 1;
        var lines = new List<Line>(segmentCount);

        for (int i = 0; i < segmentCount; i++)
        {
            XYZ from = pts[i];
            XYZ to = i == segmentCount - 1 ? pts[0] : pts[i + 1];
            lines.Add(Line.CreateBound(from, to));
        }

        return lines;
    }

    /// <summary>
    /// Returns the 8 corner vertices of <paramref name="bbox"/> in bottom-then-top order:
    /// <c>[BLF, BRF, TRF, TLF, BLB, BRB, TRB, TLB]</c>.
    /// </summary>
    public static XYZ[] ComputeVertices(this BoundingBoxXYZ bbox)
    {
        XYZ mn = bbox.Min, mx = bbox.Max;
        return
        [
            new XYZ(mn.X, mn.Y, mn.Z), new XYZ(mn.X, mx.Y, mn.Z),
            new XYZ(mx.X, mx.Y, mn.Z), new XYZ(mx.X, mn.Y, mn.Z),
            new XYZ(mn.X, mn.Y, mx.Z), new XYZ(mn.X, mx.Y, mx.Z),
            new XYZ(mx.X, mx.Y, mx.Z), new XYZ(mx.X, mn.Y, mx.Z),
        ];
    }

    /// <summary>
    /// Returns the 8 corner vertices of <paramref name="outline"/> in bottom-then-top order.
    /// </summary>
    public static XYZ[] ComputeVertices(this Outline outline)
    {
        XYZ mn = outline.MinimumPoint, mx = outline.MaximumPoint;
        return
        [
            new XYZ(mn.X, mn.Y, mn.Z), new XYZ(mn.X, mx.Y, mn.Z),
            new XYZ(mx.X, mx.Y, mn.Z), new XYZ(mx.X, mn.Y, mn.Z),
            new XYZ(mn.X, mn.Y, mx.Z), new XYZ(mn.X, mx.Y, mx.Z),
            new XYZ(mx.X, mx.Y, mx.Z), new XYZ(mx.X, mn.Y, mx.Z),
        ];
    }

    /// <summary>
    /// Returns the total arc-length of a polyline through <paramref name="points"/>
    /// (sum of distances between consecutive points).
    /// Returns <c>0</c> for sequences with fewer than two points.
    /// </summary>
    public static double GetPathLength(this IEnumerable<XYZ> points)
    {
        double total = 0;
        XYZ? prev = null;
        foreach (XYZ p in points)
        {
            if (prev is not null)
                total += p.DistanceTo(prev);
            prev = p;
        }
        return total;
    }

    /// <summary>
    /// Returns the signed distance from <paramref name="plane"/> to point <paramref name="p"/>.
    /// Positive when <paramref name="p"/> is on the side the normal points toward.
    /// </summary>
    public static double SignedDistanceTo(this Plane plane, XYZ p)
    {
        XYZ delta = p - plane.Origin;
        return plane.Normal.DotProduct(delta);
    }

    /// <summary>
    /// Projects <paramref name="p"/> orthogonally onto <paramref name="plane"/>.
    /// </summary>
    public static XYZ ProjectOnto(this Plane plane, XYZ p)
    {
        double distance = plane.SignedDistanceTo(p);
        return p - distance * plane.Normal;
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to all 8 corners of <paramref name="bbox"/> and
    /// returns a new axis-aligned bounding box that encloses the transformed corners.
    /// Returns <c>null</c> when either argument is <c>null</c>.
    /// </summary>
    public static BoundingBoxXYZ? GetTransformed(this BoundingBoxXYZ bbox, Transform transform)
    {
        if (bbox is null || transform is null)
            return null;

        XYZ mn = bbox.Min, mx = bbox.Max;
        XYZ[] corners = new[]
        {
            new XYZ(mn.X, mn.Y, mn.Z), new XYZ(mn.X, mn.Y, mx.Z),
            new XYZ(mn.X, mx.Y, mn.Z), new XYZ(mn.X, mx.Y, mx.Z),
            new XYZ(mx.X, mn.Y, mn.Z), new XYZ(mx.X, mn.Y, mx.Z),
            new XYZ(mx.X, mx.Y, mn.Z), new XYZ(mx.X, mx.Y, mx.Z),
        }.Select(transform.OfPoint).ToArray();

        return new BoundingBoxXYZ
        {
            Min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
            Max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)),
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the two axis-aligned bounding boxes overlap in all three axes.
    /// </summary>
    public static bool Intersects(this BoundingBoxXYZ first, BoundingBoxXYZ second) =>
        first is not null && second is not null &&
        first.Min.X <= second.Max.X && first.Max.X >= second.Min.X &&
        first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y &&
        first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;

    /// <summary>
    /// Returns <c>true</c> when the two axis-aligned bounding boxes overlap in all three axes,
    /// expanding each box by <paramref name="tolerance"/> on every side before the check.
    /// </summary>
    public static bool Intersects(this BoundingBoxXYZ first, BoundingBoxXYZ second, double tolerance) =>
        first is not null && second is not null &&
        first.Min.X - tolerance <= second.Max.X + tolerance && first.Max.X + tolerance >= second.Min.X - tolerance &&
        first.Min.Y - tolerance <= second.Max.Y + tolerance && first.Max.Y + tolerance >= second.Min.Y - tolerance &&
        first.Min.Z - tolerance <= second.Max.Z + tolerance && first.Max.Z + tolerance >= second.Min.Z - tolerance;

    /// <summary>
    /// Returns the smallest axis-aligned bounding box that encloses both <paramref name="first"/>
    /// and <paramref name="second"/>.
    /// </summary>
    public static BoundingBoxXYZ Combine(this BoundingBoxXYZ first, BoundingBoxXYZ second) =>
        new()
        {
            Min = new XYZ(
                Math.Min(first.Min.X, second.Min.X),
                Math.Min(first.Min.Y, second.Min.Y),
                Math.Min(first.Min.Z, second.Min.Z)),
            Max = new XYZ(
                Math.Max(first.Max.X, second.Max.X),
                Math.Max(first.Max.Y, second.Max.Y),
                Math.Max(first.Max.Z, second.Max.Z)),
        };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="point"/> lies inside or on the boundary
    /// of <paramref name="bbox"/>.
    /// </summary>
    public static bool Contains(this BoundingBoxXYZ bbox, XYZ point) =>
        bbox is not null && point is not null &&
        point.X >= bbox.Min.X && point.X <= bbox.Max.X &&
        point.Y >= bbox.Min.Y && point.Y <= bbox.Max.Y &&
        point.Z >= bbox.Min.Z && point.Z <= bbox.Max.Z;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="other"/> is fully enclosed within
    /// <paramref name="bbox"/>.
    /// </summary>
    public static bool Contains(this BoundingBoxXYZ bbox, BoundingBoxXYZ other) =>
        bbox is not null && other is not null &&
        other.Min.X >= bbox.Min.X && other.Max.X <= bbox.Max.X &&
        other.Min.Y >= bbox.Min.Y && other.Max.Y <= bbox.Max.Y &&
        other.Min.Z >= bbox.Min.Z && other.Max.Z <= bbox.Max.Z;

    /// <summary>
    /// Returns the geometric centre of <paramref name="bbox"/>.
    /// </summary>
    public static XYZ ComputeCentroid(this BoundingBoxXYZ bbox) =>
        new XYZ(
            (bbox.Min.X + bbox.Max.X) / 2,
            (bbox.Min.Y + bbox.Max.Y) / 2,
            (bbox.Min.Z + bbox.Max.Z) / 2);

    /// <summary>
    /// Returns the volume of <paramref name="bbox"/> (width × depth × height).
    /// Returns <c>0</c> for degenerate boxes where any dimension is zero or negative.
    /// </summary>
    public static double ComputeVolume(this BoundingBoxXYZ bbox)
    {
        double dx = bbox.Max.X - bbox.Min.X;
        double dy = bbox.Max.Y - bbox.Min.Y;
        double dz = bbox.Max.Z - bbox.Min.Z;
        return dx <= 0 || dy <= 0 || dz <= 0 ? 0 : dx * dy * dz;
    }

    /// <summary>
    /// Projects <paramref name="curve"/> orthogonally onto <paramref name="plane"/>.
    /// <list type="bullet">
    ///   <item><see cref="Line"/> — reconstructed from projected endpoints.</item>
    ///   <item><see cref="Arc"/> — reconstructed through projected start, mid-tessellation, and end points.</item>
    ///   <item>Any other curve — approximated as a <see cref="NurbSpline"/> through all projected tessellation points.</item>
    /// </list>
    /// </summary>
    public static Curve ProjectOnto(this Curve curve, Plane plane)
    {
        XYZ[] pts = curve.Tessellate()
            .Select(p => plane.ProjectOnto(p))
            .ToArray();

        return curve switch
        {
            Line => Line.CreateBound(pts[0], pts[pts.Length - 1]),
            Arc => Arc.Create(pts[0], pts[pts.Length - 1], pts[pts.Length / 2]),
            _ => NurbSpline.CreateCurve(pts, Enumerable.Repeat(1.0, pts.Length).ToArray()),
        };
    }
}
