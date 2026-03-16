using System.Windows;
using Autodesk.Revit.UI;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for registering and controlling Revit dockable panes.
/// </summary>
public static class DockablePaneExtensions
{
    /// <summary>
    /// Registers a dockable pane backed by <paramref name="content"/> without requiring a
    /// manual <see cref="IDockablePaneProvider"/> implementation.
    /// </summary>
    /// <example>
    /// <code>
    /// application.RegisterDockablePane(
    ///     new DockablePaneId(new Guid("...")),
    ///     "My panel",
    ///     new MyPage(),
    ///     dockPosition: DockPosition.Right,
    ///     minimumWidth: 400,
    ///     visibleByDefault: true);
    /// </code>
    /// </example>
    public static void RegisterDockablePane(
        this UIControlledApplication application,
        DockablePaneId id,
        string title,
        FrameworkElement content,
        DockPosition dockPosition = DockPosition.Right,
        int minimumWidth = 300,
        bool visibleByDefault = false)
    {
        var data = new DockablePaneProviderData
        {
            FrameworkElement = content,
            VisibleByDefault = visibleByDefault,
            InitialState = new DockablePaneState
            {
                DockPosition = dockPosition,
                MinimumWidth = minimumWidth,
            },
        };

        application.RegisterDockablePane(id, title, new InlineProvider(data));
    }

    /// <summary>
    /// Shows the pane if it is currently hidden; hides it if it is currently shown.
    /// </summary>
    public static void Toggle(this DockablePane pane)
    {
        if (pane.IsShown())
            pane.Hide();
        else
            pane.Show();
    }

    // -------------------------------------------------------------------------

    private sealed class InlineProvider : IDockablePaneProvider
    {
        private readonly DockablePaneProviderData _data;

        internal InlineProvider(DockablePaneProviderData data) => _data = data;

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _data.FrameworkElement;
            data.VisibleByDefault = _data.VisibleByDefault;
            data.InitialState = _data.InitialState;
        }
    }
}
