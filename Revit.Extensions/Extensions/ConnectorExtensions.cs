using System;
using System.Globalization;
using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="Connector"/>.
/// </summary>
/// <remarks>
/// Revit internally parses connector dimension values using the current thread culture.
/// All setters here temporarily switch to <c>en-US</c> to avoid
/// <see cref="FormatException"/> when the system locale uses a comma as decimal separator.
/// </remarks>
public static class ConnectorExtensions
{
    /// <summary>Sets the connector radius, guarding against culture-dependent parse errors.</summary>
    public static void SetRadius(this Connector connector, double radius)
    {
        using (new CultureScope())
            connector.Radius = radius;
    }

    /// <summary>
    /// Attempts to set the connector radius.
    /// Returns <c>false</c> and leaves the connector unchanged when the operation fails.
    /// </summary>
    public static bool TrySetRadius(this Connector connector, double radius)
    {
        try { connector.SetRadius(radius); return true; }
        catch { return false; }
    }

    /// <summary>Sets the connector width, guarding against culture-dependent parse errors.</summary>
    public static void SetWidth(this Connector connector, double width)
    {
        using (new CultureScope())
            connector.Width = width;
    }

    /// <summary>Sets the connector height, guarding against culture-dependent parse errors.</summary>
    public static void SetHeight(this Connector connector, double height)
    {
        using (new CultureScope())
            connector.Height = height;
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Temporarily sets <see cref="CultureInfo.CurrentCulture"/> to <c>en-US</c>
    /// and restores the original on disposal.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
