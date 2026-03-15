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

    // Solid: face color + transparency + edge color stored together.
    private readonly List<(Solid Solid, byte FR, byte FG, byte FB, double Transparency, byte ER, byte EG, byte EB)> _solids = new();

    // Mesh: triangulated surface, flat-shaded.
    private readonly List<(Mesh Mesh, byte R, byte G, byte B)> _meshes = new();

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

    /// <summary>Draws <paramref name="points"/> as a closed polygon.</summary>
    public DrawSession DrawPolygon(IEnumerable<XYZ> points, Color? color = null)
    {
        var list = points as IList<XYZ> ?? points.ToList();
        for (int i = 0; i < list.Count; i++)
            DrawLine(list[i], list[(i + 1) % list.Count], color);
        return this;
    }

    /// <summary>Draws the 12 edges of <paramref name="bbox"/>.</summary>
    public DrawSession DrawBoundingBox(BoundingBoxXYZ bbox, Color? color = null) =>
        DrawBox(bbox.ComputeVertices(), color);

    /// <summary>Draws the 12 edges of <paramref name="outline"/>.</summary>
    public DrawSession DrawBoundingBox(Outline outline, Color? color = null) =>
        DrawBox(outline.ComputeVertices(), color);

    // -------------------------------------------------------------------------
    // Fluent drawing API — solids & meshes  (call from main / UI thread)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Draws a <see cref="Solid"/> with shaded faces and tessellated edges.
    /// Faces are rendered as <c>PositionNormal</c> triangles; edges as lines.
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
        _solids.Add((solid, fc.Red, fc.Green, fc.Blue,
                     transparency < 0.0 ? 0.0 : transparency > 1.0 ? 1.0 : transparency,
                     ec.Red, ec.Green, ec.Blue));

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
        _meshes.Add((mesh, c.Red, c.Green, c.Blue));
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
        _solids.Clear();
        _meshes.Clear();
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
        && (_lines.Count > 0 || _solids.Count > 0 || _meshes.Count > 0)
        && view is View3D;

    /// <summary>
    /// Returns <c>true</c> when any solid has transparency > 0,
    /// so Revit calls <see cref="RenderScene"/> a second time for the transparent pass.
    /// </summary>
    public bool UseInTransparentPass(View view) => !_disposed && _hasTransparency;

    /// <summary>
    /// Called from the render thread — returns a pre-computed outline.
    /// Never access <c>_lines</c> / <c>_solids</c> / <c>_meshes</c> here.
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
                    FlushTriangleBuffer(vb, vbCount, ib, ibCount, primCount, r, g, b, 0.0);
                }
            }
            else
            {
                // Transparent faces only — transparent pass
                foreach (var (vb, vbCount, ib, ibCount, primCount, r, g, b, transp) in _triBuffers)
                {
                    if (transp <= 0.0) continue;
                    FlushTriangleBuffer(vb, vbCount, ib, ibCount, primCount, r, g, b, transp);
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
        byte r, byte g, byte b, double transparency)
    {
        using var fmt = new VertexFormat(VertexFormatBits.PositionNormal);
        using var eff = new EffectInstance(VertexFormatBits.PositionNormal);
        var color = new Color(r, g, b);
        eff.SetColor(color);
        eff.SetAmbientColor(color);
        eff.SetDiffuseColor(color);
        eff.SetTransparency(transparency);
        DrawContext.FlushBuffer(
            vb, vbCount, ib, ibCount, fmt, eff,
            PrimitiveType.TriangleList, 0, primCount);
    }

    private DrawSession DrawBox(XYZ[] v, Color? color)
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
        _hasTransparency = _solids.Any(s => s.Transparency > 0.0);
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

        foreach (var (solid, _, _, _, _, _, _, _) in _solids)
        {
            try
            {
                foreach (Face face in solid.Faces)
                {
                    Mesh m = face.Triangulate();
                    if (m == null) continue;
                    foreach (XYZ v in m.Vertices) Include(v);
                }
            }
            catch { }
        }

        foreach (var (mesh, _, _, _) in _meshes)
        {
            foreach (XYZ v in mesh.Vertices) Include(v);
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
        var triGroups = new Dictionary<(byte R, byte G, byte B, double T), List<(XYZ V0, XYZ V1, XYZ V2, XYZ Normal)>>();

        // Collect solid face triangles
        foreach (var (solid, fr, fg, fb, transp, _, _, _) in _solids)
        {
            var key = (fr, fg, fb, transp);
            if (!triGroups.TryGetValue(key, out var tris))
                triGroups[key] = tris = new();

            try
            {
                foreach (Face face in solid.Faces)
                {
                    Mesh m = face.Triangulate();
                    if (m == null) continue;
                    AppendMeshTriangles(m, tris);
                }
            }
            catch { }
        }

        // Collect mesh triangles
        foreach (var (mesh, r, g, b) in _meshes)
        {
            var key = (r, g, b, 0.0);
            if (!triGroups.TryGetValue(key, out var tris))
                triGroups[key] = tris = new();
            AppendMeshTriangles(mesh, tris);
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
            int vbSize = nVerts * VertexPositionNormal.GetSizeInFloats();
            int ibSize = nPrims * IndexTriangle.GetSizeInShortInts();

            var vb = new VertexBuffer(vbSize);
            vb.Map(vbSize);
            var vs = vb.GetVertexStreamPositionNormal();
            foreach (var (v0, v1, v2, n) in tris)
            {
                vs.AddVertex(new VertexPositionNormal(v0, n));
                vs.AddVertex(new VertexPositionNormal(v1, n));
                vs.AddVertex(new VertexPositionNormal(v2, n));
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

    /// <summary>Tessellates a <see cref="Mesh"/> into flat-shaded triangles.</summary>
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

            XYZ edge1 = v1 - v0;
            XYZ edge2 = v2 - v0;
            XYZ cross = edge1.CrossProduct(edge2);

            if (cross.GetLength() < 1e-10) continue; // degenerate triangle
            tris.Add((v0, v1, v2, cross.Normalize()));
        }
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
