using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Visualization helpers that create <see cref="DirectShape"/> geometry
/// and text notes in a Revit view.
/// </summary>
/// <remarks>
/// <b>All methods must be called inside an active <see cref="Transaction"/>.</b>
/// <code>
/// using var t = new Transaction(doc, "Draw");
/// t.Start();
/// doc.DrawLine(pt1, pt2, DrawExtensions.Red);
/// doc.DrawPoint(pt);
/// t.Commit();
/// </code>
/// </remarks>
public static class DrawExtensions
{
    // -------------------------------------------------------------------------
    // Predefined Revit colors
    // -------------------------------------------------------------------------

    public static Color White => new(255, 255, 255);
    public static Color Black => new(0, 0, 0);
    public static Color Blue => new(0, 0, 255);
    public static Color Red => new(255, 0, 0);
    public static Color Green => new(0, 255, 0);
    public static Color DarkGreen => new(0, 128, 0);
    public static Color Purple => new(255, 0, 255);
    public static Color Cyan => new(0, 255, 255);
    public static Color Orange => new(255, 153, 76);
    public static Color DarkBlue => new(0, 0, 102);

    // -------------------------------------------------------------------------
    // DrawLine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="DirectShape"/> line in the model.
    /// Returns <c>null</c> when the line is degenerate (zero length).
    /// </summary>
    public static DirectShape? DrawLine(this Document document,
        Line line,
        Color? color = null,
        View? view = null)
    {
        if (line.Length <= 0)
            return null;

        DirectShape shape = document.CreateDirectShape(line);
        shape.SetName("DebugLine");
        ApplyColor(shape, document, color ?? DarkBlue, view);
        return shape;
    }

    /// <summary>Creates a <see cref="DirectShape"/> line between two points.</summary>
    public static DirectShape? DrawLine(this Document document,
        XYZ point1,
        XYZ point2,
        Color? color = null,
        View? view = null)
    {
        if (point1.IsAlmostEqualTo(point2))
            return null;

        return document.DrawLine(Line.CreateBound(point1, point2), color, view);
    }

    // -------------------------------------------------------------------------
    // DrawCross
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates two perpendicular <see cref="DirectShape"/> lines centred on
    /// <paramref name="point"/> along the X and Y axes.
    /// </summary>
    /// <param name="radius">length in feet</param>
    public static void DrawCross(this Document document,
        XYZ point,
        double radius = 0.328084,
        Color? color = null,
        View? view = null)
    {
        Color c = color ?? DarkBlue;
        document.DrawLine(point - XYZ.BasisX * radius, point + XYZ.BasisX * radius, c, view);
        document.DrawLine(point - XYZ.BasisY * radius, point + XYZ.BasisY * radius, c, view);
    }

    // -------------------------------------------------------------------------
    // DrawPoint
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a sphere <see cref="DirectShape"/> at <paramref name="point"/>,
    /// optionally labelled with a <see cref="TextNote"/>.
    /// </summary>
    /// <param name="radius">radius in feet</param>
    public static DirectShape DrawPoint(this Document document,
        XYZ point,
        string? label = null,
        double radius = 0.328084,
        Color? color = null,
        View? view = null)
    {
        Color c = color ?? DarkBlue;

        Arc arc = Arc.Create(
            point + XYZ.BasisZ * radius,
            point - XYZ.BasisZ * radius,
            point + XYZ.BasisX * radius);

        Line axis = Line.CreateBound(
            point - XYZ.BasisZ * radius,
            point + XYZ.BasisZ * radius);

        CurveLoop profile = new();
        profile.Append(axis);
        profile.Append(arc);

        Solid sphere = GeometryCreationUtilities.CreateRevolvedGeometry(new Frame(point, XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ),
            [profile], -Math.PI, Math.PI);

        DirectShape shape = document.CreateDirectShape(sphere);
        shape.SetName("DebugPoint");

        FillPatternElement? fill = FindSolidFillPattern(document);
        if (fill is not null)
            ApplyColorWithFill(shape, document, c, fill, view);
        else
            ApplyColor(shape, document, c, view);

        if (!string.IsNullOrEmpty(label))
            document.DrawText(label!, point, c, view);

        return shape;
    }

    // -------------------------------------------------------------------------
    // DrawText
    // -------------------------------------------------------------------------

    /// <summary>Creates a <see cref="TextNote"/> at <paramref name="point"/>.</summary>
    public static TextNote DrawText(this Document document,
        string text,
        XYZ point,
        Color? color = null,
        View? view = null)
    {
        view ??= document.ActiveView;
        ElementId typeId = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        TextNote note = TextNote.Create(document, view.Id, point, text, typeId);
        view.SetElementOverrides(note.Id, BuildOverride(color ?? DarkBlue));
        return note;
    }

    // -------------------------------------------------------------------------
    // DrawPolygon
    // -------------------------------------------------------------------------

    /// <summary>Creates <see cref="DirectShape"/> lines for each segment in <paramref name="lines"/>.</summary>
    public static void DrawPolygon(this Document document,
        IEnumerable<Line> lines,
        Color? color = null,
        View? view = null)
    {
        foreach (Line line in lines)
            document.DrawLine(line, color ?? Black, view);
    }

    /// <summary>
    /// Creates <see cref="DirectShape"/> lines connecting <paramref name="points"/>
    /// as a closed polygon.
    /// </summary>
    public static void DrawPolygon(
        this Document document,
        IEnumerable<XYZ> points,
        Color? color = null,
        View? view = null) =>
        document.DrawPolygon(points.GetPolygonLines(), color ?? Blue, view);

    // -------------------------------------------------------------------------
    // Draw bounding boxes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Draws the 12 edges of <paramref name="bbox"/> as <see cref="DirectShape"/> lines.
    /// Pass <paramref name="drawPoints"/> to also mark the 8 corners with spheres.
    /// </summary>
    public static void Draw(this BoundingBoxXYZ bbox,
        Document document,
        Color? color = null,
        bool drawPoints = false,
        View? view = null) =>
        DrawBox(document, bbox.ComputeVertices(), color ?? Green, drawPoints, view);

    /// <summary>
    /// Draws the 12 edges of <paramref name="outline"/> as <see cref="DirectShape"/> lines.
    /// </summary>
    public static void DrawDebug(
        this Outline outline,
        Document document,
        Color? color = null,
        bool drawPoints = false,
        View? view = null) =>
        DrawBox(document, outline.ComputeVertices(), color ?? Green, drawPoints, view);

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void DrawBox(Document document, XYZ[] v, Color color, bool drawPoints, View? view)
    {
        // Bottom face, top face
        document.DrawPolygon([v[0], v[1], v[2], v[3]], color, view);
        document.DrawPolygon([v[4], v[5], v[6], v[7]], color, view);

        // Vertical edges
        foreach ((int b, int t) in new[] { (0, 4), (1, 5), (2, 6), (3, 7) })
            document.DrawLine(v[b], v[t], color, view);

        if (drawPoints)
            foreach (XYZ pt in v)
                document.DrawPoint(pt, color: color, view: view);
    }

    private static void ApplyColor(DirectShape shape, Document document, Color color, View? view)
    {
        view ??= document.ActiveView;
        view.SetElementOverrides(shape.Id, BuildOverride(color));
    }

    private static void ApplyColorWithFill(
        DirectShape shape, Document document, Color color, FillPatternElement fill, View? view)
    {
        view ??= document.ActiveView;
        var ogs = new OverrideGraphicSettings();
        ogs.SetSurfaceForegroundPatternColor(color);
        ogs.SetSurfaceForegroundPatternId(fill.Id);
        ogs.SetSurfaceForegroundPatternVisible(true);
        ogs.SetSurfaceBackgroundPatternColor(color);
        ogs.SetSurfaceBackgroundPatternId(fill.Id);
        ogs.SetSurfaceBackgroundPatternVisible(true);
        view.SetElementOverrides(shape.Id, ogs);
    }

    private static OverrideGraphicSettings BuildOverride(Color color)
    {
        var ogs = new OverrideGraphicSettings();
        ogs.SetSurfaceForegroundPatternColor(color);
        ogs.SetSurfaceBackgroundPatternColor(color);
        ogs.SetSurfaceForegroundPatternVisible(true);
        ogs.SetSurfaceBackgroundPatternVisible(true);
        ogs.SetCutLineColor(color);
        ogs.SetProjectionLineColor(color);
        return ogs;
    }

    private static FillPatternElement? FindSolidFillPattern(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(FillPatternElement))
            .WhereElementIsNotElementType()
            .OfType<FillPatternElement>()
            .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
}
