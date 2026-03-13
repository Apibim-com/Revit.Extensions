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
/// Register once (e.g. at command start) and dispose when done.
/// <code>
/// using var draw = new DrawSession(uiApp);
/// draw.DrawLine(pt1, pt2, DrawExtensions.Red)
///     .DrawPoint(center)
///     .DrawBoundingBox(bbox);
/// // geometry disappears when draw is disposed at end of using block
/// </code>
/// </remarks>
public sealed class DrawSession : IDirectContext3DServer, IDisposable
{
    // -------------------------------------------------------------------------
    // Primitive storage
    // -------------------------------------------------------------------------

    // Each line stored as (from, to, R, G, B) to avoid Color equality issues
    private readonly List<(XYZ From, XYZ To, byte R, byte G, byte B)> _lines = new();

    // Cached GPU buffers grouped by colour — rebuilt when _isDirty is true
    private readonly List<(VertexBuffer Vb, int VbCount, IndexBuffer Ib, int IbCount, int PrimCount, byte R, byte G, byte B)> _buffers = new();
    private bool _isDirty;

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
    // Fluent drawing API
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
    public Outline? GetBoundingBox(View view) => null;

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (_isDirty)
                RebuildBuffers();

            DrawContext.SetWorldTransform(Transform.Identity);

            foreach (var (vb, vbCount, ib, ibCount, primCount, r, g, b) in _buffers)
            {
                var fmt = new VertexFormat(VertexFormatBits.Position);
                var eff = new EffectInstance(VertexFormatBits.Position);
                eff.SetColor(new Color(r, g, b));
                DrawContext.FlushBuffer(
                    vb, vbCount, ib, ibCount, fmt, eff,
                    PrimitiveType.LineList, 0, primCount);
            }
        }
        catch
        {
            // Never crash the Revit render loop
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

    private void Invalidate()
    {
        _isDirty = true;
        _uiApp.ActiveUIDocument?.RefreshActiveView();
    }

    private void RebuildBuffers()
    {
        _buffers.Clear();

        // Group lines by colour to minimise FlushBuffer calls
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
            vb.Map(nVerts * VertexPosition.GetSizeInFloats());
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
    }
}
