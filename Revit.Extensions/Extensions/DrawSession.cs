using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;

namespace Revit.Extensions;

/// <summary>
/// Renders truly transient geometry via <see cref="IDirectContext3DServer"/>.
/// Supports lines, solids (faces + edges) and meshes.
/// No model elements are created and no <see cref="Transaction"/> is required.
/// Geometry is visible until the instance is disposed or <see cref="Clear"/> is called.
/// </summary>
/// <remarks>
/// <b>Registration</b> is NOT automatic — call
/// <see cref="UIControlledApplicationExtensions.RegisterDrawSession"/> from
/// <c>IExternalApplication.OnStartup</c>:
/// <code>
/// // OnStartup
/// App.Session = application.RegisterDrawSession();
///
/// // OnShutdown
/// App.Session?.Dispose();
/// </code>
///
/// The session must outlive the command that draws into it — do NOT wrap it in a
/// <c>using</c> statement inside <c>IExternalCommand.Execute()</c>.
/// Revit renders the view after <c>Execute()</c> returns, so the session must
/// still be alive at that point.
/// <code>
/// // ✓ correct — stored at application scope, disposed in OnShutdown
/// App.Session.DrawLine(pt1, pt2);
///
/// // ✗ wrong — disposed before Revit renders
/// using var s = new DrawSession(); s.DrawLine(pt1, pt2);
/// </code>
/// </remarks>
public sealed class DrawSession : IDirectContext3DServer, IDisposable
{
    // -------------------------------------------------------------------------
    // Primitive storage  (main thread only)
    // -------------------------------------------------------------------------

    private readonly List<(XYZ From, XYZ To, byte R, byte G, byte B)> _lines = new();

    // Pre-computed triangle data — populated on the main thread inside DrawSolid/DrawMesh.
    // Revit geometry API (face.Triangulate, face.ComputeNormal, mesh.get_Triangle) has
    // main-thread affinity and must NOT be called from the render thread.
    private readonly List<(byte R, byte G, byte B, double Transparency, List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)> Tris)> _triData = new();

    // Cache of normalised glyph triangles: key=(text, fontFamily),
    // value=list of (x0,y0, x1,y1, x2,y2) in font-height-normalised coords (height=1.0, Y-up).
    // Populated on the main thread in DrawText; safe to read from the same thread thereafter.
    private static readonly Dictionary<(string, string), List<(float, float, float, float, float, float)>>
        _textCache = new Dictionary<(string, string), List<(float, float, float, float, float, float)>>();

    // -------------------------------------------------------------------------
    // GPU buffers  (rebuilt on render thread when _isDirty is true)
    // -------------------------------------------------------------------------

    // Line buffers — VertexFormatBits.Position + LineList
    private readonly List<(VertexBuffer Vb, int VbCount, IndexBuffer Ib, int IbCount, int PrimCount, byte R, byte G, byte B)> _lineBuffers = new();

    // Triangle buffers — VertexFormatBits.PositionNormal + TriangleList
    // Transparency field: 0.0 = opaque, 1.0 = fully transparent.
    private readonly List<(VertexBuffer Vb, int VbCount, IndexBuffer Ib, int IbCount, int PrimCount, byte R, byte G, byte B, double Transparency)> _triBuffers = new();

    private volatile bool _isDirty;
    private volatile bool _hasTransparency;

    // Bounding box cached on the main thread; read on the render thread.
    // Per the DirectContext3D spec, GetBoundingBox() may be called from a
    // different thread — never compute it inline from _lines/_solids/_meshes there.
    private volatile Outline? _cachedOutline;

    private readonly Guid _serverId = Guid.NewGuid();
    private UIApplication? _uiApp;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the draw session.
    /// </summary>
    /// <param name="uiApp">
    /// Optional. Required only for automatic view refresh after drawing calls.
    /// Can be omitted when constructing via
    /// <see cref="UIControlledApplicationExtensions.RegisterDrawSession"/> (called in
    /// <c>OnStartup</c>) and supplied later via <see cref="SetUIApplication"/>.
    /// </param>
    /// <remarks>
    /// Registration with Revit's DirectContext3D service is NOT performed automatically.
    /// Use <see cref="UIControlledApplicationExtensions.RegisterDrawSession"/> instead.
    /// </remarks>
    public DrawSession(UIApplication? uiApp = null)
    {
        _uiApp = uiApp;
    }

    /// <summary>
    /// Supplies the <see cref="UIApplication"/> needed for automatic view refresh.
    /// Call this once from the first <see cref="Autodesk.Revit.UI.IExternalCommand"/> that uses the session.
    /// </summary>
    public void SetUIApplication(UIApplication uiApp) =>
        _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));

    // -------------------------------------------------------------------------
    // Fluent drawing API — lines  (call from main / UI thread)
    // -------------------------------------------------------------------------

    /// <summary>Draws a line between two points.</summary>
    public DrawSession DrawLine(XYZ from, XYZ to, Color? color = null)
    {
        if (from.IsAlmostEqualTo(to)) return this;
        Color c = color ?? DrawExtensions.DarkBlue;
        _lines.Add((from, to, c.Red, c.Green, c.Blue));
        Invalidate();
        return this;
    }

    /// <summary>Draws a <see cref="Line"/>.</summary>
    public DrawSession DrawLine(Line line, Color? color = null) =>
        DrawLine(line.GetEndPoint(0), line.GetEndPoint(1), color);

    /// <summary>Draws a 3-axis cross centred on <paramref name="point"/>.</summary>
    public DrawSession DrawCross(XYZ point, double radius = 0.328084, Color? color = null)
    {
        Color c = color ?? DrawExtensions.DarkBlue;
        return DrawLine(point - XYZ.BasisX * radius, point + XYZ.BasisX * radius, c)
              .DrawLine(point - XYZ.BasisY * radius, point + XYZ.BasisY * radius, c)
              .DrawLine(point - XYZ.BasisZ * radius, point + XYZ.BasisZ * radius, c);
    }

    /// <summary>Draws a point marker (3-axis cross) at <paramref name="point"/>.</summary>
    public DrawSession DrawPoint(XYZ point, double radius = 0.328084, Color? color = null) =>
        DrawCross(point, radius, color);

    /// <summary>
    /// Draws a point marker (3-axis cross) with a text label next to it.
    /// </summary>
    /// <param name="point">Point position in world space.</param>
    /// <param name="label">Text label to display next to the point.</param>
    /// <param name="radius">Cross half-size in Revit internal units (feet). Default ≈ 0.1 m.</param>
    /// <param name="color">Color for both cross and label. Defaults to <see cref="DrawExtensions.DarkBlue"/>.</param>
    /// <param name="normal">
    /// Face normal that defines the plane in which the label lies.
    /// <c>null</c> defaults to <see cref="XYZ.BasisZ"/> (XY plane, horizontal text).
    /// </param>
    public DrawSession DrawPoint(XYZ point, string label, double radius = 0.328084,
        Color? color = null, XYZ? normal = null)
    {
        DrawCross(point, radius, color);
        double textHeight = radius * 2.5;
        var (right, up) = ComputeTextFrame((normal ?? XYZ.BasisZ).Normalize());
        XYZ textOrigin = point + right * (radius * 1.5) + up * (-textHeight * 0.5);
        return DrawText(label, textOrigin, textHeight, color, normal: normal);
    }

    /// <summary>Draws <paramref name="points"/> as a closed polygon.</summary>
    public DrawSession DrawPolygon(IEnumerable<XYZ> points, Color? color = null)
    {
        var list = points as IList<XYZ> ?? points.ToList();
        for (int i = 0; i < list.Count; i++)
            DrawLine(list[i], list[(i + 1) % list.Count], color);
        return this;
    }

    /// <summary>Draws the 12 edges of <paramref name="bbox"/> (wireframe only).</summary>
    public DrawSession DrawBoundingBox(BoundingBoxXYZ bbox, Color? color = null) =>
        DrawWireBox(bbox.ComputeVertices(), color);

    /// <summary>Draws the 12 edges of <paramref name="outline"/> (wireframe only).</summary>
    public DrawSession DrawBoundingBox(Outline outline, Color? color = null) =>
        DrawWireBox(outline.ComputeVertices(), color);

    // -------------------------------------------------------------------------
    // Fluent drawing API — solids & meshes  (call from main / UI thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Draws a <see cref="Solid"/> with shaded faces and tessellated edges.
    /// Faces are rendered as <c>PositionNormalColored</c> triangles; edges as lines.
    /// </summary>
    /// <param name="solid">The solid to render.</param>
    /// <param name="faceColor">Face fill color. Defaults to <see cref="DrawExtensions.Blue"/>.</param>
    /// <param name="edgeColor">Edge line color. Defaults to <see cref="DrawExtensions.DarkBlue"/>.</param>
    /// <param name="transparency">Face transparency 0.0 (opaque) – 1.0 (invisible).</param>
    public DrawSession DrawSolid(Solid solid,
        Color? faceColor = null,
        Color? edgeColor = null,
        double transparency = 0.0)
    {
        if (solid is null || solid.Volume <= 0) return this;

        Color fc = faceColor ?? DrawExtensions.Blue;
        Color ec = edgeColor ?? DrawExtensions.DarkBlue;
        double t = transparency < 0.0 ? 0.0 : transparency > 1.0 ? 1.0 : transparency;

        // Pre-compute face triangles HERE on the main thread.
        // Revit geometry API (Triangulate, ComputeNormal) has main-thread affinity —
        // calling it from the render thread (RebuildBuffers) returns garbage silently,
        // producing zero-length normals and black faces.
        var tris = new List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>();
        foreach (Face face in solid.Faces)
        {
            try
            {
                Mesh m = face.Triangulate();
                if (m == null) continue;
                AppendFaceTriangles(face, m, tris);
            }
            catch { }
        }
        if (tris.Count > 0)
            _triData.Add((fc.Red, fc.Green, fc.Blue, t, tris));

        // Tessellate edges as lines — they share the line buffer pipeline.
        foreach (Edge edge in solid.Edges)
        {
            IList<XYZ> pts = edge.Tessellate();
            for (int i = 0; i < pts.Count - 1; i++)
                if (!pts[i].IsAlmostEqualTo(pts[i + 1]))
                    _lines.Add((pts[i], pts[i + 1], ec.Red, ec.Green, ec.Blue));
        }

        Invalidate();
        return this;
    }

    /// <summary>
    /// Draws a <see cref="Mesh"/> as flat-shaded triangles (per-triangle normals).
    /// </summary>
    /// <param name="mesh">The mesh to render.</param>
    /// <param name="color">Fill color. Defaults to <see cref="DrawExtensions.Blue"/>.</param>
    public DrawSession DrawMesh(Mesh mesh, Color? color = null)
    {
        if (mesh is null || mesh.NumTriangles == 0) return this;

        Color c = color ?? DrawExtensions.Blue;

        // Pre-compute triangles on the main thread — same reason as DrawSolid.
        var tris = new List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>();
        AppendMeshTriangles(mesh, tris);
        if (tris.Count > 0)
            _triData.Add((c.Red, c.Green, c.Blue, 0.0, tris));

        Invalidate();
        return this;
    }

    /// <summary>
    /// Draws a filled axis-aligned box defined by <paramref name="min"/> and <paramref name="max"/> corners.
    /// </summary>
    /// <param name="min">Minimum corner (world space).</param>
    /// <param name="max">Maximum corner (world space).</param>
    /// <param name="faceColor">Face fill color. Defaults to <see cref="DrawExtensions.Blue"/>.</param>
    /// <param name="edgeColor">Edge line color. Defaults to <see cref="DrawExtensions.DarkBlue"/>.</param>
    /// <param name="transparency">Face transparency 0.0 (opaque) – 1.0 (invisible).</param>
    public DrawSession DrawBox(XYZ min, XYZ max,
        Color? faceColor = null,
        Color? edgeColor = null,
        double transparency = 0.0)
    {
        Color fc = faceColor ?? DrawExtensions.Blue;
        Color ec = edgeColor ?? DrawExtensions.DarkBlue;
        double t = transparency < 0.0 ? 0.0 : transparency > 1.0 ? 1.0 : transparency;

        var tris = new List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>();
        AppendBoxTriangles(min, max, tris);
        if (tris.Count > 0)
            _triData.Add((fc.Red, fc.Green, fc.Blue, t, tris));

        // 12 wireframe edges
        XYZ v0 = min,                            v1 = new XYZ(min.X, max.Y, min.Z);
        XYZ v2 = new XYZ(max.X, max.Y, min.Z),  v3 = new XYZ(max.X, min.Y, min.Z);
        XYZ v4 = new XYZ(min.X, min.Y, max.Z),  v5 = new XYZ(min.X, max.Y, max.Z);
        XYZ v6 = max,                            v7 = new XYZ(max.X, min.Y, max.Z);
        byte er = ec.Red, eg = ec.Green, eb = ec.Blue;
        _lines.Add((v0, v1, er, eg, eb)); _lines.Add((v1, v2, er, eg, eb));
        _lines.Add((v2, v3, er, eg, eb)); _lines.Add((v3, v0, er, eg, eb));
        _lines.Add((v4, v5, er, eg, eb)); _lines.Add((v5, v6, er, eg, eb));
        _lines.Add((v6, v7, er, eg, eb)); _lines.Add((v7, v4, er, eg, eb));
        _lines.Add((v0, v4, er, eg, eb)); _lines.Add((v1, v5, er, eg, eb));
        _lines.Add((v2, v6, er, eg, eb)); _lines.Add((v3, v7, er, eg, eb));

        Invalidate();
        return this;
    }

    /// <summary>
    /// Draws a filled box from a <see cref="BoundingBoxXYZ"/>.
    /// Uses <see cref="BoundingBoxXYZ.Min"/> and <see cref="BoundingBoxXYZ.Max"/> directly
    /// (same convention as <see cref="DrawBoundingBox(BoundingBoxXYZ,Color?)"/>).
    /// </summary>
    public DrawSession DrawBox(BoundingBoxXYZ bbox,
        Color? faceColor = null,
        Color? edgeColor = null,
        double transparency = 0.0) =>
        DrawBox(bbox.Min, bbox.Max, faceColor, edgeColor, transparency);

    /// <summary>
    /// Draws a filled box from an <see cref="Outline"/>.
    /// </summary>
    public DrawSession DrawBox(Outline outline,
        Color? faceColor = null,
        Color? edgeColor = null,
        double transparency = 0.0) =>
        DrawBox(outline.MinimumPoint, outline.MaximumPoint, faceColor, edgeColor, transparency);

    /// <summary>
    /// Draws a filled UV sphere centred at <paramref name="center"/> with the given <paramref name="radius"/>.
    /// </summary>
    /// <param name="center">Sphere centre (world space).</param>
    /// <param name="radius">Sphere radius (Revit internal units — feet).</param>
    /// <param name="faceColor">Face fill color. Defaults to <see cref="DrawExtensions.Blue"/>.</param>
    /// <param name="edgeColor">
    /// Color of the three great-circle edge lines (XY / XZ / YZ planes).
    /// Pass <c>null</c> to skip edges.
    /// </param>
    /// <param name="transparency">Face transparency 0.0 (opaque) – 1.0 (invisible).</param>
    public DrawSession DrawSphere(XYZ center, double radius,
        Color? faceColor = null,
        Color? edgeColor = null,
        double transparency = 0.0)
    {
        if (radius <= 0) return this;

        Color fc = faceColor ?? DrawExtensions.Blue;
        Color ec = edgeColor ?? DrawExtensions.DarkBlue;
        double t = transparency < 0.0 ? 0.0 : transparency > 1.0 ? 1.0 : transparency;

        var tris = new List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>();
        AppendSphereTriangles(center, radius, tris);
        if (tris.Count > 0)
            _triData.Add((fc.Red, fc.Green, fc.Blue, t, tris));

        // Three great circles as wireframe edges
        if (edgeColor != null)
            AppendSphereEdges(center, radius, ec);

        Invalidate();
        return this;
    }

    /// <summary>
    /// Draws <paramref name="text"/> as filled triangles tessellated from a real system font.
    /// </summary>
    /// <remarks>
    /// Uses <c>System.Drawing.GraphicsPath</c> to extract glyph outlines from the specified
    /// font, then <c>LibTessDotNet</c> (EvenOdd winding) to tessellate them into triangles —
    /// correctly handling inner holes in letters like O, A, B, D, Р, О, etc.
    /// Results are cached per <c>(text, fontFamily)</c> so repeated calls are cheap.
    /// </remarks>
    /// <param name="text">Text to render. Any Unicode characters supported by the font.</param>
    /// <param name="origin">Bottom-left corner of the first character (world space).</param>
    /// <param name="height">Character height in Revit internal units (feet). Default 0.5 ft ≈ 15 cm.</param>
    /// <param name="color">Fill color. Defaults to <see cref="DrawExtensions.DarkBlue"/>.</param>
    /// <param name="fontFamily">
    /// System font family name (e.g. "Arial", "Times New Roman", "Consolas").
    /// Falls back to generic sans-serif if the font is not installed.
    /// </param>
    /// <param name="transparency">Transparency 0.0 (opaque) – 1.0 (invisible).</param>
    /// <param name="normal">
    /// Face normal of the plane in which the text lies.
    /// <c>null</c> defaults to <see cref="XYZ.BasisZ"/> (XY plane, horizontal text).
    /// The local X/Y axes are computed automatically via <see cref="ComputeTextFrame"/>.
    /// </param>
    public DrawSession DrawText(string text, XYZ origin,
        double height = 0.5,
        Color? color = null,
        string fontFamily = "Arial",
        double transparency = 0.0,
        XYZ? normal = null)
    {
        if (string.IsNullOrEmpty(text) || height <= 0) return this;

        var cacheKey = (text, fontFamily);
        if (!_textCache.TryGetValue(cacheKey, out var normalized))
        {
            try   { normalized = BuildTextTriangles(text, fontFamily); }
            catch { normalized = new List<(float, float, float, float, float, float)>(); }
            _textCache[cacheKey] = normalized;
        }
        if (normalized.Count == 0) return this;

        Color c = color ?? DrawExtensions.DarkBlue;
        double t = transparency < 0.0 ? 0.0 : transparency > 1.0 ? 1.0 : transparency;

        XYZ n = (normal ?? XYZ.BasisZ).Normalize();
        var (right, up) = ComputeTextFrame(n);

        var tris = new List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>(normalized.Count);
        foreach (var (x0, y0, x1, y1, x2, y2) in normalized)
        {
            tris.Add((
                origin + right * (x0 * height) + up * (y0 * height),
                origin + right * (x1 * height) + up * (y1 * height),
                origin + right * (x2 * height) + up * (y2 * height),
                n));
        }
        _triData.Add((c.Red, c.Green, c.Blue, t, tris));
        Invalidate();
        return this;
    }

    // -------------------------------------------------------------------------
    // Session management
    // -------------------------------------------------------------------------

    /// <summary>Removes all drawn primitives without disposing the session.</summary>
    public DrawSession Clear()
    {
        _lines.Clear();
        _triData.Clear();
        Invalidate();
        return this;
    }

    // -------------------------------------------------------------------------
    // IDirectContext3DServer
    // -------------------------------------------------------------------------

    public Guid GetServerId() => _serverId;

    public ExternalServiceId GetServiceId() =>
        ExternalServices.BuiltInExternalServices.DirectContext3DService;

    public string GetName() => "DrawSession";
    public string GetDescription() => "Revit.Extensions transient geometry renderer";
    public string GetVendorId() => "Revit.Extensions";
    public string GetApplicationId() => "Revit.Extensions.DrawSession";
    public string GetSourceId() => _serverId.ToString();
    public bool UsesHandles() => false;

    public bool CanExecute(View view) =>
        !_disposed
        && (_lines.Count > 0 || _triData.Count > 0)
        && view is View3D;

    /// <summary>
    /// Returns <c>true</c> when any solid has transparency > 0,
    /// so Revit calls <see cref="RenderScene"/> a second time for the transparent pass.
    /// </summary>
    public bool UseInTransparentPass(View view) => !_disposed && _hasTransparency;

    /// <summary>
    /// Called from the render thread — returns a pre-computed outline.
    /// Never access <c>_lines</c> / <c>_triData</c> here (main-thread data).
    /// </summary>
    public Outline? GetBoundingBox(View view) => _cachedOutline;

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (_isDirty)
                RebuildBuffers();

            bool isTransparentPass = DrawContext.IsTransparentPass();
            DrawContext.SetWorldTransform(Transform.Identity);

            // Lines and opaque faces — opaque pass only.
            if (!isTransparentPass)
            {
                // Lines
                foreach (var (vb, vbCount, ib, ibCount, primCount, r, g, b) in _lineBuffers)
                {
                    using var fmt = new VertexFormat(VertexFormatBits.Position);
                    using var eff = new EffectInstance(VertexFormatBits.Position);
                    eff.SetColor(new Color(r, g, b));
                    eff.SetTransparency(0.0);
                    DrawContext.FlushBuffer(
                        vb, vbCount, ib, ibCount, fmt, eff,
                        PrimitiveType.LineList, 0, primCount);
                }

                // Opaque triangle faces (solids + meshes)
                foreach (var (vb, vbCount, ib, ibCount, primCount, r, g, b, transp) in _triBuffers)
                {
                    if (transp > 0.0) continue;
                    FlushTriangleBuffer(vb, vbCount, ib, ibCount, primCount, r, g, b, 0.0, displayStyle);
                }
            }
            else
            {
                // Transparent faces only — transparent pass
                foreach (var (vb, vbCount, ib, ibCount, primCount, r, g, b, transp) in _triBuffers)
                {
                    if (transp <= 0.0) continue;
                    FlushTriangleBuffer(vb, vbCount, ib, ibCount, primCount, r, g, b, transp, displayStyle);
                }
            }
        }
        catch
        {
            // Never crash the Revit render loop.
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void FlushTriangleBuffer(
        VertexBuffer vb, int vbCount,
        IndexBuffer ib, int ibCount, int primCount,
        byte r, byte g, byte b, double transparency,
        DisplayStyle displayStyle)
    {
        using var fmt = new VertexFormat(VertexFormatBits.PositionNormalColored);
        using var eff = new EffectInstance(VertexFormatBits.PositionNormalColored);
        var color = new Color(r, g, b);
        eff.SetColor(color);
        eff.SetDiffuseColor(color);
        eff.SetTransparency(transparency);
        if (displayStyle == DisplayStyle.HLR)
        {
            eff.SetSpecularColor(color);
            eff.SetAmbientColor(color);
            eff.SetEmissiveColor(color);
        }
        DrawContext.FlushBuffer(
            vb, vbCount, ib, ibCount, fmt, eff,
            PrimitiveType.TriangleList, 0, primCount);
    }

    private DrawSession DrawWireBox(XYZ[] v, Color? color)
    {
        DrawPolygon(new[] { v[0], v[1], v[2], v[3] }, color);
        DrawPolygon(new[] { v[4], v[5], v[6], v[7] }, color);
        foreach ((int b, int t) in new[] { (0, 4), (1, 5), (2, 6), (3, 7) })
            DrawLine(v[b], v[t], color);
        return this;
    }

    /// <summary>
    /// Called from the main thread after every mutation.
    /// Updates the cached outline (safe to read from the render thread),
    /// marks buffers dirty, and requests a view refresh.
    /// </summary>
    private void Invalidate()
    {
        _cachedOutline = ComputeOutline();
        _hasTransparency = _triData.Any(t => t.Transparency > 0.0);
        _isDirty = true;
        _uiApp?.ActiveUIDocument?.RefreshActiveView();
    }

    /// <summary>Computes a world-space bounding outline from all current primitives.</summary>
    private Outline? ComputeOutline()
    {
        bool any = false;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        void Include(XYZ p)
        {
            any = true;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;
        }

        foreach (var (from, to, _, _, _) in _lines)
        {
            Include(from);
            Include(to);
        }

        foreach (var (_, _, _, _, tris) in _triData)
        {
            foreach (var (v0, v1, v2, _) in tris)
            {
                Include(v0);
                Include(v1);
                Include(v2);
            }
        }

        if (!any) return null;

        const double eps = 0.01;
        return new Outline(
            new XYZ(minX - eps, minY - eps, minZ - eps),
            new XYZ(maxX + eps, maxY + eps, maxZ + eps));
    }

    private void RebuildBuffers()
    {
        DisposeBuffers();

        // ---- Line buffers ----
        var lineGroups = new Dictionary<(byte R, byte G, byte B), List<(XYZ From, XYZ To)>>();
        foreach (var (from, to, r, g, b) in _lines)
        {
            var key = (r, g, b);
            if (!lineGroups.TryGetValue(key, out var list))
                lineGroups[key] = list = new();
            list.Add((from, to));
        }

#if REVIT_2024 || REVIT_2023 || REVIT_2022 || REVIT_2021 || REVIT_2020
        foreach (var lgItem in lineGroups)
        {
            var lKey = lgItem.Key;
            var lines = lgItem.Value;
#else
        foreach (var (lKey, lines) in lineGroups)
        {
#endif
            int nVerts = lines.Count * 2;
            int nPrims = lines.Count;
            int vbSize = nVerts * VertexPosition.GetSizeInFloats();
            int ibSize = nPrims * IndexLine.GetSizeInShortInts();

            var vb = new VertexBuffer(vbSize);
            vb.Map(vbSize);
            var vs = vb.GetVertexStreamPosition();
            foreach (var (from, to) in lines)
            {
                vs.AddVertex(new VertexPosition(from));
                vs.AddVertex(new VertexPosition(to));
            }
            vb.Unmap();

            var ib = new IndexBuffer(ibSize);
            ib.Map(ibSize);
            var ixs = ib.GetIndexStreamLine();
            for (int i = 0; i < nPrims; i++)
                ixs.AddLine(new IndexLine(i * 2, i * 2 + 1));
            ib.Unmap();

            _lineBuffers.Add((vb, nVerts, ib, ibSize, nPrims, lKey.R, lKey.G, lKey.B));
        }

        // ---- Triangle buffers (solid faces + meshes) ----
        // Key: (R, G, B, Transparency) — group to minimise FlushBuffer calls.
        // Triangle data was pre-computed on the main thread in DrawSolid/DrawMesh —
        // no Revit geometry API calls needed here (render-thread safe).
        var triGroups = new Dictionary<(byte R, byte G, byte B, double T), List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>>();

        foreach (var (r, g, b, transp, tris) in _triData)
        {
            var key = (r, g, b, transp);
            if (!triGroups.TryGetValue(key, out var group))
                triGroups[key] = group = new();
            group.AddRange(tris);
        }

#if REVIT_2024 || REVIT_2023 || REVIT_2022 || REVIT_2021 || REVIT_2020
        foreach (var tgItem in triGroups)
        {
            var tKey = tgItem.Key;
            var tris = tgItem.Value;
#else
        foreach (var (tKey, tris) in triGroups)
        {
#endif
            if (tris.Count == 0) continue;

            int nVerts = tris.Count * 3;
            int nPrims = tris.Count;
            int vbSize = nVerts * VertexPositionNormalColored.GetSizeInFloats();
            int ibSize = nPrims * IndexTriangle.GetSizeInShortInts();

            // Color is embedded per-vertex — required for PositionNormalColored format.
            var vertColor = new ColorWithTransparency(
                tKey.R, tKey.G, tKey.B,
                (uint)(tKey.T * 255));

            var vb = new VertexBuffer(vbSize);
            vb.Map(vbSize);
            var vs = vb.GetVertexStreamPositionNormalColored();
            foreach (var (v0, v1, v2, n) in tris)
            {
                vs.AddVertex(new VertexPositionNormalColored(v0, n, vertColor));
                vs.AddVertex(new VertexPositionNormalColored(v1, n, vertColor));
                vs.AddVertex(new VertexPositionNormalColored(v2, n, vertColor));
            }
            vb.Unmap();

            var ib = new IndexBuffer(ibSize);
            ib.Map(ibSize);
            var ixs = ib.GetIndexStreamTriangle();
            for (int i = 0; i < nPrims; i++)
                ixs.AddTriangle(new IndexTriangle(i * 3, i * 3 + 1, i * 3 + 2));
            ib.Unmap();

            _triBuffers.Add((vb, nVerts, ib, ibSize, nPrims, tKey.R, tKey.G, tKey.B, tKey.T));
        }

        _isDirty = false;
    }

    /// <summary>
    /// Appends triangles from a solid <see cref="Face"/> tessellation.
    /// Computes a reference outward normal via <see cref="Face.ComputeNormal"/> at the
    /// face UV centroid, then flips each triangle's computed normal if it points inward.
    /// This is necessary because Revit's tessellation winding order is not guaranteed
    /// to produce outward-facing normals.
    /// </summary>
    private static void AppendFaceTriangles(
        Face face,
        Mesh mesh,
        List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)> tris)
    {
        // Compute a reference outward normal at the face UV centroid.
        XYZ? faceRef = null;
        try
        {
            BoundingBoxUV uvBox = face.GetBoundingBox();
            UV centerUV = new UV(
                (uvBox.Min.U + uvBox.Max.U) / 2.0,
                (uvBox.Min.V + uvBox.Max.V) / 2.0);
            faceRef = face.ComputeNormal(centerUV);
        }
        catch { }

        for (int i = 0; i < mesh.NumTriangles; i++)
        {
            MeshTriangle tri = mesh.get_Triangle(i);
            XYZ v0 = tri.get_Vertex(0);
            XYZ v1 = tri.get_Vertex(1);
            XYZ v2 = tri.get_Vertex(2);

            XYZ cross = (v1 - v0).CrossProduct(v2 - v0);
            if (cross.GetLength() < 1e-10) continue;

            XYZ normal = cross.Normalize();

            // If we have a reference normal, ensure triangle normal agrees with it.
            if (faceRef != null && normal.DotProduct(faceRef) < 0)
                normal = normal.Negate();

            tris.Add((v0, v1, v2, normal));
        }
    }

    /// <summary>
    /// Appends triangles from a standalone <see cref="Mesh"/> using flat normals
    /// computed from the cross product of each triangle's edges.
    /// </summary>
    private static void AppendMeshTriangles(
        Mesh mesh,
        List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)> tris)
    {
        for (int i = 0; i < mesh.NumTriangles; i++)
        {
            MeshTriangle tri = mesh.get_Triangle(i);
            XYZ v0 = tri.get_Vertex(0);
            XYZ v1 = tri.get_Vertex(1);
            XYZ v2 = tri.get_Vertex(2);

            XYZ cross = (v1 - v0).CrossProduct(v2 - v0);
            if (cross.GetLength() < 1e-10) continue;
            tris.Add((v0, v1, v2, cross.Normalize()));
        }
    }

    /// <summary>
    /// Generates 12 triangles (6 quad-faces × 2) for an axis-aligned box.
    /// Normals are the axis-unit vectors — no Revit API calls required.
    /// </summary>
    private static void AppendBoxTriangles(XYZ min, XYZ max,
        List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)> tris)
    {
        XYZ v0 = min,                            v1 = new XYZ(min.X, max.Y, min.Z);
        XYZ v2 = new XYZ(max.X, max.Y, min.Z),  v3 = new XYZ(max.X, min.Y, min.Z);
        XYZ v4 = new XYZ(min.X, min.Y, max.Z),  v5 = new XYZ(min.X, max.Y, max.Z);
        XYZ v6 = max,                            v7 = new XYZ(max.X, min.Y, max.Z);

        void AddQuad(XYZ a, XYZ b, XYZ c, XYZ d, XYZ n)
        {
            tris.Add((a, b, c, n));
            tris.Add((a, c, d, n));
        }

        AddQuad(v0, v1, v2, v3, -XYZ.BasisZ); // bottom
        AddQuad(v4, v7, v6, v5,  XYZ.BasisZ); // top
        AddQuad(v0, v3, v7, v4, -XYZ.BasisY); // front
        AddQuad(v1, v5, v6, v2,  XYZ.BasisY); // back
        AddQuad(v0, v4, v5, v1, -XYZ.BasisX); // left
        AddQuad(v3, v2, v6, v7,  XYZ.BasisX); // right
    }

    /// <summary>
    /// Generates triangles for a UV sphere with 16 latitude × 32 longitude rings.
    /// Per-vertex normals are the unit direction from <paramref name="center"/> to the vertex.
    /// </summary>
    private static void AppendSphereTriangles(XYZ center, double radius,
        List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)> tris)
    {
        const int latSegs = 16;
        const int lonSegs = 32;

        XYZ UnitPt(double theta, double phi) =>
            new XYZ(
                Math.Sin(theta) * Math.Cos(phi),
                Math.Sin(theta) * Math.Sin(phi),
                Math.Cos(theta));

        for (int i = 0; i < latSegs; i++)
        {
            double t0 = Math.PI * i / latSegs;
            double t1 = Math.PI * (i + 1) / latSegs;

            for (int j = 0; j < lonSegs; j++)
            {
                double p0 = 2.0 * Math.PI * j / lonSegs;
                double p1 = 2.0 * Math.PI * (j + 1) / lonSegs;

                XYZ n00 = UnitPt(t0, p0), n01 = UnitPt(t0, p1);
                XYZ n10 = UnitPt(t1, p0), n11 = UnitPt(t1, p1);
                XYZ v00 = center + n00 * radius, v01 = center + n01 * radius;
                XYZ v10 = center + n10 * radius, v11 = center + n11 * radius;

                // Upper triangle (skip at north pole where v00==v01)
                if (i > 0)
                    tris.Add((v00, v10, v11, (n00 + n10 + n11).Normalize()));
                // Lower triangle (skip at south pole where v10==v11)
                if (i < latSegs - 1)
                    tris.Add((v00, v11, v01, (n00 + n11 + n01).Normalize()));
            }
        }
    }

    /// <summary>
    /// Appends three great-circle edge rings (XY, XZ, YZ planes) as line segments.
    /// </summary>
    private void AppendSphereEdges(XYZ center, double radius, Color color)
    {
        const int segs = 48;
        byte r = color.Red, g = color.Green, b = color.Blue;

        for (int i = 0; i < segs; i++)
        {
            double a0 = 2.0 * Math.PI * i / segs;
            double a1 = 2.0 * Math.PI * (i + 1) / segs;

            // XY plane (equator)
            _lines.Add((
                center + new XYZ(Math.Cos(a0) * radius, Math.Sin(a0) * radius, 0),
                center + new XYZ(Math.Cos(a1) * radius, Math.Sin(a1) * radius, 0),
                r, g, b));
            // XZ plane
            _lines.Add((
                center + new XYZ(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius),
                center + new XYZ(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius),
                r, g, b));
            // YZ plane
            _lines.Add((
                center + new XYZ(0, Math.Cos(a0) * radius, Math.Sin(a0) * radius),
                center + new XYZ(0, Math.Cos(a1) * radius, Math.Sin(a1) * radius),
                r, g, b));
        }
    }

    // -------------------------------------------------------------------------
    // Text tessellation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes a local right/up frame for text rendering given a face <paramref name="normal"/>.
    /// <list type="bullet">
    ///   <item>Horizontal surfaces (<paramref name="normal"/> ≈ ±Z): uses world X/Y.</item>
    ///   <item>All other normals: <c>up</c> is the world-Z direction projected onto the plane;
    ///         <c>right</c> is derived via <c>BasisZ × normal</c>.</item>
    /// </list>
    /// </summary>
    private static (XYZ Right, XYZ Up) ComputeTextFrame(XYZ n)
    {
        if (Math.Abs(n.DotProduct(XYZ.BasisZ)) > 0.99)
            return (XYZ.BasisX, XYZ.BasisY);   // horizontal surface — keep standard XY orientation

        // Project world Z onto the plane to get "up", then derive "right"
        XYZ right = XYZ.BasisZ.CrossProduct(n).Normalize();
        XYZ up    = n.CrossProduct(right).Normalize();
        return (right, up);
    }

    /// <summary>
    /// Builds a list of normalised triangles for <paramref name="text"/> using a real system
    /// font.  Coordinates are in font-height space: character height = 1.0, Y-up, X origin at
    /// the left edge of the first glyph.  Results are suitable for caching in
    /// <see cref="_textCache"/>.
    /// </summary>
    private static List<(float, float, float, float, float, float)> BuildTextTriangles(
        string text, string fontFamily)
    {
        const float RefSize = 100f;
        var contours = new List<List<System.Drawing.PointF>>();

        using (var gdiFamily = GetFontFamily(fontFamily))
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            path.AddString(text, gdiFamily, (int)System.Drawing.FontStyle.Regular,
                RefSize, System.Drawing.PointF.Empty,
                System.Drawing.StringFormat.GenericTypographic);
            path.Flatten(null, 0.5f);
            ExtractContours(path, contours);
        }

        if (contours.Count == 0)
            return new List<(float, float, float, float, float, float)>();

        // Bounding box of all contour points (GDI+ Y-down coordinate space)
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var contour in contours)
            foreach (var pt in contour)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }

        float h = maxY - minY;
        if (h < 1e-6f)
            return new List<(float, float, float, float, float, float)>();
        float invH = 1.0f / h;

        // Feed contours to LibTessDotNet with EvenOdd winding.
        // EvenOdd correctly handles glyph holes (letters O, A, B, D, Р, О, etc.).
        var tess = new LibTessDotNet.Tess();
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            var verts = new LibTessDotNet.ContourVertex[contour.Count];
            for (int i = 0; i < contour.Count; i++)
            {
                // Normalise to height=1.0 and flip Y (GDI+ Y-down → Revit Y-up)
                float nx = (contour[i].X - minX) * invH;
                float ny = (maxY - contour[i].Y) * invH;
                verts[i] = new LibTessDotNet.ContourVertex
                {
                    Position = new LibTessDotNet.Vec3 { X = nx, Y = ny, Z = 0 }
                };
            }
            tess.AddContour(verts, LibTessDotNet.ContourOrientation.Original);
        }

        tess.Tessellate(LibTessDotNet.WindingRule.EvenOdd,
            LibTessDotNet.ElementType.Polygons, 3);

        var result = new List<(float, float, float, float, float, float)>(tess.ElementCount);
        for (int i = 0; i < tess.ElementCount; i++)
        {
            int i0 = tess.Elements[i * 3];
            int i1 = tess.Elements[i * 3 + 1];
            int i2 = tess.Elements[i * 3 + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0) continue;
            var p0 = tess.Vertices[i0].Position;
            var p1 = tess.Vertices[i1].Position;
            var p2 = tess.Vertices[i2].Position;
            result.Add((p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y));
        }
        return result;
    }

    /// <summary>
    /// Parses <see cref="System.Drawing.Drawing2D.GraphicsPath"/> path data into a list of
    /// polyline contours.  Each sub-path (delimited by <c>PathPointType.Start</c> points or
    /// <c>CloseSubpath</c> flags) becomes one entry in <paramref name="contours"/>.
    /// </summary>
    private static void ExtractContours(
        System.Drawing.Drawing2D.GraphicsPath path,
        List<List<System.Drawing.PointF>> contours)
    {
        var points = path.PathPoints;
        var types  = path.PathTypes;
        if (points == null || points.Length == 0) return;

        List<System.Drawing.PointF>? current = null;
        for (int i = 0; i < points.Length; i++)
        {
            byte baseType = (byte)(types[i] & 0x07);
            if (baseType == 0 || current == null) // PathPointType.Start
            {
                current = new List<System.Drawing.PointF>();
                contours.Add(current);
            }

            current.Add(points[i]);

            // CloseSubpath flag (0x80) — next point starts a fresh contour
            if ((types[i] & 0x80) != 0)
                current = null;
        }
    }

    /// <summary>
    /// Returns a <see cref="System.Drawing.FontFamily"/> for <paramref name="name"/>,
    /// falling back to Arial and then the generic sans-serif family if the requested
    /// font is not installed.  The caller is responsible for disposing the returned instance.
    /// </summary>
    private static System.Drawing.FontFamily GetFontFamily(string name)
    {
        try
        {
            var family = new System.Drawing.FontFamily(name);
            if (family.IsStyleAvailable(System.Drawing.FontStyle.Regular))
                return family;
            family.Dispose();
        }
        catch { }

        try { return new System.Drawing.FontFamily("Arial"); } catch { }
        return new System.Drawing.FontFamily(
            System.Drawing.Text.GenericFontFamilies.SansSerif);
    }

    private void RegisterImpl()
    {
        if (ExternalServiceRegistry.GetService(
                ExternalServices.BuiltInExternalServices.DirectContext3DService)
            is not MultiServerService service) return;

        service.AddServer(this);
        IList<Guid> activeIds = service.GetActiveServerIds();
        if (!activeIds.Contains(_serverId))
        {
            activeIds.Add(_serverId);
            service.SetActiveServers(activeIds);
        }
    }

    private void Unregister()
    {
        if (ExternalServiceRegistry.GetService(
                ExternalServices.BuiltInExternalServices.DirectContext3DService)
            is not MultiServerService service) return;

        IList<Guid> activeIds = service.GetActiveServerIds();
        if (activeIds.Remove(_serverId))
            service.SetActiveServers(activeIds);

        try { service.RemoveServer(_serverId); } catch { }

        _uiApp?.ActiveUIDocument?.RefreshActiveView();
    }

    private void DisposeBuffers()
    {
        foreach (var (vb, _, ib, _, _, _, _, _) in _lineBuffers)
        {
            vb.Dispose();
            ib.Dispose();
        }
        _lineBuffers.Clear();

        foreach (var (vb, _, ib, _, _, _, _, _, _) in _triBuffers)
        {
            vb.Dispose();
            ib.Dispose();
        }
        _triBuffers.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        DisposeBuffers();
    }
}
