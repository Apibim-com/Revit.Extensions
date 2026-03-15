using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="UIControlledApplication"/>.
/// </summary>
public static class UIControlledApplicationExtensions
{
    /// <summary>
    /// Creates a <see cref="DrawSession"/> and registers it with Revit's
    /// DirectContext3D service. Call once from
    /// <see cref="IExternalApplication.OnStartup"/>.
    /// </summary>
    /// <remarks>
    /// The returned session is already active — Revit will call
    /// <see cref="DrawSession.RenderScene"/> as soon as a 3D view is displayed
    /// and geometry has been added via the fluent drawing API.
    /// Dispose the session in <see cref="IExternalApplication.OnShutdown"/> to
    /// unregister the server and release GPU buffers.
    /// <code>
    /// // App.cs — OnStartup
    /// _drawSession = application.RegisterDrawSession();
    ///
    /// // App.cs — OnShutdown
    /// _drawSession?.Dispose();
    /// </code>
    /// </remarks>
    /// <param name="application">The controlled application from <c>OnStartup</c>.</param>
    /// <returns>The registered <see cref="DrawSession"/> instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the DirectContext3D service is unavailable.
    /// </exception>
    public static DrawSession RegisterDrawSession(this UIControlledApplication application)
    {
        if (ExternalServiceRegistry.GetService(
                ExternalServices.BuiltInExternalServices.DirectContext3DService)
            is not MultiServerService service)
            throw new InvalidOperationException(
                "DirectContext3D service is not available. " +
                "Ensure this is called from IExternalApplication.OnStartup.");

        var session = new DrawSession();

        // Per Revit API docs: AddServer first, then GetActiveServerIds.
        service.AddServer(session);

        IList<Guid> activeIds = service.GetActiveServerIds();
        if (!activeIds.Contains(session.GetServerId()))
        {
            activeIds.Add(session.GetServerId());
            service.SetActiveServers(activeIds);
        }

        return session;
    }
}
