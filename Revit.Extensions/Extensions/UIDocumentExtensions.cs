using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="UIDocument"/>.
/// </summary>
public static class UIDocumentExtensions
{
    /// <summary>
    /// Prompts the user to pick an element of type <typeparamref name="T"/> from the model.
    /// Returns <c>null</c> when the user cancels or picks an incompatible element.
    /// </summary>
    /// <example>
    /// <code>
    /// Wall? wall = uidoc.PickElementByClass&lt;Wall&gt;();
    /// </code>
    /// </example>
    public static T? PickElementByClass<T>(this UIDocument uidoc)
        where T : Element
    {
        try
        {
            Reference? reference = uidoc.Selection.PickObject(
                ObjectType.Element,
                new ClassFilter<T>(),
                "Pick element");

            if (reference is null)
                return null;

            Element element = uidoc.Document.GetElement(reference);

            if (element is null || !element.IsValidObject)
                return null;

            return element as T;
        }
        catch
        {
            // User cancelled (OperationCanceledException) or selection failed.
            return null;
        }
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the elements currently selected in the model.
    /// </summary>
    public static IEnumerable<Element> GetSelectedElements(this UIDocument uidoc)
    {
        foreach (ElementId id in uidoc.Selection.GetElementIds())
            yield return uidoc.Document.GetElement(id);
    }

    // -------------------------------------------------------------------------

    private sealed class ClassFilter<T> : ISelectionFilter
        where T : Element
    {
        public bool AllowElement(Element elem) => elem is T;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
