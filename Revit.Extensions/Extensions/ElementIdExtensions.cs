using System;
using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for <see cref="ElementId"/>.
/// </summary>
public static class ElementIdExtensions
{
    /// <summary>
    /// Returns <c>true</c> when the <see cref="ElementId"/> is non-null and not
    /// <see cref="ElementId.InvalidElementId"/>.
    /// </summary>
    public static bool IsValid(this ElementId elementId) =>
        elementId is not null && elementId != ElementId.InvalidElementId;

    /// <summary>
    /// Returns <c>true</c> when the <see cref="ElementId"/> is null or
    /// <see cref="ElementId.InvalidElementId"/>.
    /// </summary>
    public static bool IsInvalid(this ElementId elementId) =>
        !elementId.IsValid();

    /// <summary>
    /// Interprets the numeric value of <paramref name="elementId"/> as a
    /// <see cref="BuiltInCategory"/>.
    /// </summary>
    public static BuiltInCategory ToBuiltInCategory(this ElementId elementId)
    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026 || REVIT_2027
        return (BuiltInCategory)elementId.Value;
#else
        return (BuiltInCategory)elementId.IntegerValue;
#endif
    }

    /// <summary>
    /// Interprets the numeric value of <paramref name="elementId"/> as a
    /// <see cref="BuiltInParameter"/>.
    /// </summary>
    public static BuiltInParameter ToBuiltInParameter(this ElementId elementId)
    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026 || REVIT_2027
        return (BuiltInParameter)elementId.Value;
#else
        return (BuiltInParameter)elementId.IntegerValue;
#endif
    }

    /// <summary>
    /// Returns <c>true</c> when the id is positive — i.e. a user-created element
    /// rather than a built-in constant.
    /// </summary>
    public static bool IsUserCreated(this ElementId elementId)
    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026 || REVIT_2027
        return elementId is not null && elementId.Value > ElementId.InvalidElementId.Value;
#else
        return elementId is not null && elementId.IntegerValue > ElementId.InvalidElementId.IntegerValue;
#endif
    }

    /// <summary>
    /// Returns <c>true</c> when the id is a negative built-in constant
    /// (e.g. a <see cref="BuiltInCategory"/> or <see cref="BuiltInParameter"/> value).
    /// </summary>
    public static bool IsBuiltIn(this ElementId elementId)
    {
#if REVIT_2024 || REVIT_2025 || REVIT_2026 || REVIT_2027
        return elementId is not null && elementId.Value < ElementId.InvalidElementId.Value;
#else
        return elementId is not null && elementId.IntegerValue < ElementId.InvalidElementId.IntegerValue;
#endif
    }

    /// <summary>
    /// Fetches the element from <paramref name="document"/>, cast to
    /// <typeparamref name="T"/>. Returns <c>null</c> when the cast fails or
    /// the element does not exist.
    /// </summary>
    public static T? GetElement<T>(this ElementId elementId, Document document)
        where T : Element
    {
        if (elementId is null) throw new ArgumentNullException(nameof(elementId));
        if (document is null) throw new ArgumentNullException(nameof(document));
        return document.GetElement(elementId) as T;
    }

    /// <summary>
    /// Tries to fetch and cast the element. Returns <c>false</c> when the element
    /// does not exist or is not of type <typeparamref name="T"/>.
    /// </summary>
    public static bool TryGetElement<T>(this ElementId elementId, Document document, out T? element)
        where T : Element
    {
        element = elementId.GetElement<T>(document);
        return element is not null;
    }

    // -------------------------------------------------------------------------
    // Factory helpers — reverse direction: enum → ElementId
    // -------------------------------------------------------------------------

    /// <summary>Creates an <see cref="ElementId"/> from a <see cref="BuiltInCategory"/>.</summary>
    public static ElementId ToElementId(this BuiltInCategory category) =>
        new ElementId(category);

    /// <summary>Creates an <see cref="ElementId"/> from a <see cref="BuiltInParameter"/>.</summary>
    public static ElementId ToElementId(this BuiltInParameter parameter) =>
        new ElementId(parameter);

    /// <summary>
    /// Fetches the element from <paramref name="document"/> without a type cast.
    /// Returns <c>null</c> when the id is invalid or the element does not exist.
    /// </summary>
    public static Element? ToElement(this ElementId elementId, Document document) =>
        elementId.IsValid() ? document.GetElement(elementId) : null;
}
