using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for building Revit ribbon tabs, panels, and buttons.
/// </summary>
public static class RibbonExtensions
{
    // -------------------------------------------------------------------------
    // Panel
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns an existing ribbon panel named <paramref name="panelName"/> on
    /// <paramref name="tabName"/>, or creates the tab (if absent) and panel.
    /// </summary>
    public static RibbonPanel GetOrCreatePanel(
        this UIControlledApplication app,
        string tabName,
        string panelName)
    {
        try
        {
            RibbonPanel? existing = app.GetRibbonPanels(tabName)
                .FirstOrDefault(p => p.Name == panelName);

            if (existing is not null)
                return existing;
        }
        catch
        {
            // Tab does not exist yet — create it below.
        }

        try { app.CreateRibbonTab(tabName); } catch { /* already exists */ }

        return app.CreateRibbonPanel(tabName, panelName);
    }

    // -------------------------------------------------------------------------
    // Push button
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a <see cref="PushButton"/> wired to <typeparamref name="TCommand"/> to
    /// <paramref name="panel"/>.
    /// </summary>
    /// <typeparam name="TCommand">
    /// An <see cref="IExternalCommand"/> implementation in any loaded assembly.
    /// </typeparam>
    /// <param name="panel">The ribbon panel to add the button to.</param>
    /// <param name="text">Label shown under the button.</param>
    /// <param name="tooltip">Optional tooltip text.</param>
    /// <param name="largeImage">Optional 32×32 image shown on large buttons.</param>
    /// <param name="image">Optional 16×16 image shown on small/stacked buttons.</param>
    /// <example>
    /// <code>
    /// panel.AddPushButton&lt;MyCommand&gt;(
    ///     "Export",
    ///     tooltip: "Exports the model to IFC",
    ///     largeImage: svgPath.ToRibbonImage(Colors.SteelBlue));
    /// </code>
    /// </example>
    public static PushButton AddPushButton<TCommand>(
        this RibbonPanel panel,
        string text,
        string? tooltip = null,
        BitmapSource? largeImage = null,
        BitmapSource? image = null)
        where TCommand : class, IExternalCommand
    {
        Type commandType = typeof(TCommand);

        Assembly assembly = Assembly.GetAssembly(commandType)
            ?? throw new InvalidOperationException(
                $"Unable to locate assembly for {commandType.FullName}.");

        var data = new PushButtonData(
            name: commandType.FullName!,
            text: text,
            assemblyName: assembly.Location,
            className: commandType.FullName!);

        var button = panel.AddItem(data) as PushButton
            ?? throw new InvalidOperationException("Failed to create PushButton.");

        if (tooltip is not null)     button.ToolTip = tooltip;
        if (largeImage is not null)  button.LargeImage = largeImage;
        if (image is not null)       button.Image = image;

        return button;
    }
#if REVIT_2024 || REVIT_2023 || REVIT_2022 || REVIT_2021 || REVIT_2020
#else
    /// <summary>
    /// Adds a <see cref="CommandMenuItem"/> wired to <typeparamref name="TCommand"/>
    /// with availability controlled by <typeparamref name="TAvailability"/> to a
    /// right-click <see cref="ContextMenu"/>.
    /// </summary>
    /// <typeparam name="TCommand">An <see cref="IExternalCommand"/> implementation.</typeparam>
    /// <typeparam name="TAvailability">
    /// An <see cref="IExternalCommandAvailability"/> implementation with a public
    /// parameterless constructor.
    /// </typeparam>
    public static CommandMenuItem AddMenuItem<TCommand, TAvailability>(
        this ContextMenu menu,
        string title,
        string? tooltip = null)
        where TCommand      : class, IExternalCommand
        where TAvailability : class, IExternalCommandAvailability, new()
    {
        Type commandType = typeof(TCommand);

        Assembly assembly = Assembly.GetAssembly(commandType)
            ?? throw new InvalidOperationException(
                $"Unable to locate assembly for {commandType.FullName}.");

        var menuItem = new CommandMenuItem(title, commandType.FullName!, assembly.Location);
        menuItem.SetAvailabilityClassName(typeof(TAvailability).FullName!);

        if (tooltip is not null)
            menuItem.SetToolTip(tooltip);

        menu.AddItem(menuItem);
        return menuItem;
    }
#endif
    // -------------------------------------------------------------------------
    // Image helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders an SVG path data string into a square <see cref="BitmapSource"/>.
    /// The path is uniformly scaled and centred inside the canvas.
    /// </summary>
    /// <param name="svgPathData">SVG <c>d</c> attribute value, e.g. <c>"M10 10 L90 90 …"</c>.</param>
    /// <param name="color">Fill colour of the rendered shape.</param>
    /// <param name="size">Canvas side length in pixels (default 32 for large ribbon images).</param>
    /// <example>
    /// <code>
    /// BitmapSource icon = "M60.6 10.7H42.3…".ToRibbonImage(Colors.SteelBlue, 32);
    /// </code>
    /// </example>
    public static BitmapSource ToRibbonImage(this string svgPathData, Color color, double size = 32)
    {
        Geometry geometry = Geometry.Parse(svgPathData);
        System.Windows.Rect bounds = geometry.Bounds;

        double scale = Math.Min(size / bounds.Width, size / bounds.Height);

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(
            -bounds.X * scale + (size - bounds.Width  * scale) / 2,
            -bounds.Y * scale + (size - bounds.Height * scale) / 2));

        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Brush    = new SolidColorBrush(color),
            Pen      = null,
        };

        var group = new DrawingGroup { Transform = transform };
        group.Children.Add(drawing);

        var visual = new DrawingVisual();
        using (DrawingContext ctx = visual.RenderOpen())
            ctx.DrawDrawing(group);

        var bitmap = new RenderTargetBitmap(
            (int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Loads a <see cref="BitmapSource"/> from an image file on disk (PNG, BMP, ICO, etc.).
    /// Returns <c>null</c> when the file does not exist.
    /// </summary>
    /// <param name="filePath">Absolute path to the image file.</param>
    public static BitmapSource? ImageFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var image = new BitmapImage(new Uri(filePath, UriKind.Absolute));
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Decodes a <see cref="BitmapSource"/> from a raw image byte array (PNG, BMP, ICO, etc.).
    /// Intended for images embedded as assembly resources.
    /// </summary>
    /// <param name="imageBytes">Raw bytes of the image file.</param>
    /// <example>
    /// <code>
    /// // Embed the file in the .csproj:
    /// //   &lt;EmbeddedResource Include="Resources\icon_32.png" /&gt;
    /// byte[] bytes = Resources.icon_32;          // strongly-typed resource
    /// BitmapSource icon = RibbonExtensions.ImageFromBytes(bytes);
    /// </code>
    /// </example>
    public static BitmapSource ImageFromBytes(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
