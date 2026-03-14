using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;

namespace Revit.Extensions;

/// <summary>
/// Renders truly transient geometry via <see cref="IDirectContext3DServer"/>.
/// No model elements are created and no <see cref="Transaction"/> is required.
/// Geometry is visible until the instance is disposed.
/// </summary>
/// <remarks>
/// The session must outlive the command that creates it — do NOT wrap it in a
/// <c>using</c> statement inside <c>IExternalCommand.Execute()</c>.
/// Revit renders the view after <c>Execute()</c> returns, so the session must
/// still be alive at that point. Store it as a field on your Application or
/// command class and call <see cref="Dispose"/> explicitly when done.
/// <code>
/// // ✓ correct — stored at application scope
/// App.DrawSession = new DrawSession(uiApp);
/// App.DrawSession.DrawLine(pt1, pt2);
///
/// // ✗ wrong — disposed before Revit renders
/// using var draw = new DrawSession(uiApp);
/// draw.DrawLine(pt1, pt2);
/// </code>
/// </remarks>
public sealed class DrawSession : IDirectContext3DServer, IDisposable
{
    // -------------------------------------------------------------------------
    // Primitive storage  (main thread only)
    // -------------------------------------------------------------------------

    private readonly List<(XYZ From, XYZ To, byte R, byte G, byte B)> _lines = new();

    // -------------------------------------------------------------------------
    // GPU buffers  (rebuilt on render thread when _isDirty is true)
    // -------------------------------------------------------------------------

    private readonly List<(VertexBuffer Vb, int VbCount, IndexBuffer Ib, int IbCount, int PrimCount, byte R, byte G, byte B)> _buffers = new();
    private volatile bool _isDirty;

    // Bounding box cached on the main thread; read on the render thread.
    // Per the DirectContext3D spec, GetBoundingBox() may be called from a
    // different thread — never compute it inline from _lines there.
    private volatile Outline? _cachedOutline;

    private readonly Guid _serverId = Guid.NewGuid();
    private readonly UIApplication _uiApp;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Construction
    // -------------------------------------------------------------------------

    /// <summary>Creates the draw session and registers it with Revit's DirectContext3D service.</summary>
    public DrawSession(UIApplication uiApp)
    {
        _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
        Register();
    }

    // -------------------------------------------------------------------------
    // Fluent drawing API  (call from main / UI thread)
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

    /// <summary>Removes all drawn primitives without disposing the session.</summary>
    public DrawSession Clear()
    {
        _lines.Clear();
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

    public bool CanExecute(View view) => !_disposed && _lines.Count > 0 && view is View3D;

    public bool UseInTransparentPass(View view) => false;

    /// <summary>
    /// Called from the render thread — returns a pre-computed outline.
    /// Never access <c>_lines</c> here; it belongs to the main thread.
    /// </summary>
    public Outline? GetBoundingBox(View view) => _cachedOutline;

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            // Skip the transparent pass — we only draw opaque lines.
            if (DrawContext.IsTransparentPass())
                return;

            if (_isDirty)
                RebuildBuffers();

            DrawContext.SetWorldTransform(Transform.Identity);

            foreach (var (vb, vbCount, ib, ibCount, primCount, r, g, b) in _buffers)
            {
                using var fmt = new VertexFormat(VertexFormatBits.Position);
                using var eff = new EffectInstance(VertexFormatBits.Position);
                eff.SetColor(new Color(r, g, b));
                eff.SetTransparency(0.0);
                DrawContext.FlushBuffer(
                    vb, vbCount, ib, ibCount, fmt, eff,
                    PrimitiveType.LineList, 0, primCount);
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
        _cachedOutline = ComputeOutline();  // main thread — safe to read _lines
        _isDirty = true;
        _uiApp.ActiveUIDocument?.RefreshActiveView();
    }

    /// <summary>Computes a world-space bounding outline from the current lines.</summary>
    private Outline? ComputeOutline()
    {
        if (_lines.Count == 0) return null;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        foreach (var (from, to, _, _, _) in _lines)
        {
            minX = Math.Min(minX, Math.Min(from.X, to.X));
            minY = Math.Min(minY, Math.Min(from.Y, to.Y));
            minZ = Math.Min(minZ, Math.Min(from.Z, to.Z));
            maxX = Math.Max(maxX, Math.Max(from.X, to.X));
            maxY = Math.Max(maxY, Math.Max(from.Y, to.Y));
            maxZ = Math.Max(maxZ, Math.Max(from.Z, to.Z));
        }

        // Small epsilon prevents a degenerate outline on flat / collinear geometry.
        const double eps = 0.01;
        return new Outline(
            new XYZ(minX - eps, minY - eps, minZ - eps),
            new XYZ(maxX + eps, maxY + eps, maxZ + eps));
    }

    private void RebuildBuffers()
    {
        foreach (var (vb, _, ib, _, _, _, _, _) in _buffers)
        {
            vb.Dispose();
            ib.Dispose();
        }
        _buffers.Clear();

        // Group lines by colour to minimise FlushBuffer calls.
        var groups = new Dictionary<(byte R, byte G, byte B), List<(XYZ From, XYZ To)>>();
        foreach (var (from, to, r, g, b) in _lines)
        {
            var key = (r, g, b);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new();
            list.Add((from, to));
        }

#if REVIT_2024 || REVIT_2023 || REVIT_2022 || REVIT_2021 || REVIT_2020
        foreach (var item in groups)
        {
            var key = item.Key;
            var lines = item.Value;
#else
        foreach (var (key, lines) in groups)
        {
#endif
            int nVerts = lines.Count * 2;
            int nPrims = lines.Count;
            int ibSize = nPrims * IndexLine.GetSizeInShortInts();

            var vb = new VertexBuffer(nVerts * VertexPosition.GetSizeInFloats());
            vb.Map(0);
            var vs = vb.GetVertexStreamPosition();
            foreach (var (from, to) in lines)
            {
                vs.AddVertex(new VertexPosition(from));
                vs.AddVertex(new VertexPosition(to));
            }
            vb.Unmap();

            var ib = new IndexBuffer(ibSize);
            ib.Map(0);
            var ixs = ib.GetIndexStreamLine();
            for (int i = 0; i < nPrims; i++)
                ixs.AddLine(new IndexLine(i * 2, i * 2 + 1));
            ib.Unmap();

            _buffers.Add((vb, nVerts, ib, ibSize, nPrims, key.R, key.G, key.B));
        }

        _isDirty = false;
    }

    private void Register()
    {
        if (ExternalServiceRegistry.GetService(
                ExternalServices.BuiltInExternalServices.DirectContext3DService)
            is not MultiServerService service) return;

        service.AddServer(this);
        IList<Guid> activeIds = service.GetActiveServerIds();
        activeIds.Add(_serverId);
        service.SetActiveServers(activeIds);
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

        _uiApp.ActiveUIDocument?.RefreshActiveView();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
        foreach (var (vb, _, ib, _, _, _, _, _) in _buffers)
        {
            vb.Dispose();
            ib.Dispose();
        }
        _buffers.Clear();
    }
}
