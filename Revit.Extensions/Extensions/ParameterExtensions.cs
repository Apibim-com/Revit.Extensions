using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit.Extensions;

/// <summary>
/// Extension methods for reading Revit parameter values.
/// </summary>
public static class ParameterExtensions
{
    /// <summary>
    /// Returns the parameter value cast to <typeparamref name="T"/>.
    /// Supported types: <see cref="string"/>, <see cref="double"/>, <see cref="int"/>, <see cref="ElementId"/>.
    /// Returns <c>default</c> when the parameter has no value (<see cref="Parameter.HasValue"/> is false).
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown when <typeparamref name="T"/> does not match the parameter's storage type.
    /// </exception>
    public static T? GetValue<T>(this Parameter parameter)
    {
        if (!parameter.HasValue)
            return default;

        if (typeof(T) == typeof(string))
            return (T?)(object?)parameter.AsString();

        if (typeof(T) == typeof(double))
            return (T?)(object?)parameter.AsDouble();

        if (typeof(T) == typeof(int))
            return (T?)(object?)parameter.AsInteger();

        if (typeof(T) == typeof(ElementId))
            return (T?)(object?)parameter.AsElementId();

        throw new NotSupportedException(
            $"Type '{typeof(T)}' is not supported. Use string, double, int, or ElementId.");
    }

    /// <summary>
    /// Looks up a parameter by name and returns its value as <typeparamref name="T"/>.
    /// Returns <c>default</c> when the parameter is not found or has no value.
    /// </summary>
    public static T? GetParameterValue<T>(this Element element, string parameterName)
    {
        Parameter? param = element.LookupParameter(parameterName);
        return param is null ? default : param.GetValue<T>();
    }

    /// <summary>
    /// Looks up a built-in parameter and returns its value as <typeparamref name="T"/>.
    /// Returns <c>default</c> when the parameter is not found or has no value.
    /// </summary>
    public static T? GetParameterValue<T>(this Element element, BuiltInParameter builtInParameter)
    {
        Parameter? param = element.get_Parameter(builtInParameter);
        return param is null ? default : param.GetValue<T>();
    }

    /// <summary>
    /// Looks up a parameter by name on the element instance first, then on its type.
    /// Returns <c>null</c> when the element is invalid or the parameter is not found on either.
    /// </summary>
    public static Parameter? GetParameterFromInstanceOrType(this Element element, string parameterName)
    {
        if (!element.IsValidObject)
            return null;

        Parameter? param = element.LookupParameter(parameterName);
        if (param is not null)
            return param;

        ElementId typeId = element.GetTypeId();
        if (typeId is null)
            return null;

        return element.Document?.GetElement(typeId)?.LookupParameter(parameterName);
    }

    /// <summary>
    /// Returns a sorted list of shared/project parameter names that are filterable for
    /// <paramref name="category"/> and whose data type matches <paramref name="specTypeId"/>.
    /// Built-in parameters (negative <see cref="ElementId"/>) are excluded.
    /// </summary>
    /// <example>
    /// <code>
    /// doc.GetParameterNamesBySpec(BuiltInCategory.OST_Walls, SpecTypeId.String.Text)
    /// doc.GetParameterNamesBySpec(BuiltInCategory.OST_Walls, SpecTypeId.Length)
    /// </code>
    /// </example>
    public static List<string> GetParameterNamesBySpec(
        this Document document,
        BuiltInCategory category,
        ForgeTypeId specTypeId)
    {
        ElementId categoryId = Category.GetCategory(document, category).Id;

        return ParameterFilterUtilities
            .GetFilterableParametersInCommon(document, [categoryId])
#if REVIT_2024 || REVIT_2025 || REVIT_2026 || REVIT_2027
            .Where(id => id.Value >= 0)
#else
            .Where(id => id.IntegerValue >= 0)
#endif
            .Select(id => document.GetElement(id) as ParameterElement)
            .Where(pe => pe?.GetDefinition().GetDataType() == specTypeId)
            .Select(pe => pe!.Name)
            .OrderBy(name => name)
            .ToList();
    }

    /// <summary>
    /// Returns a sorted list of parameter names on <paramref name="familyInstance"/>
    /// whose data type matches <paramref name="specTypeId"/>.
    /// When <paramref name="includeTypeParameters"/> is <c>true</c>, type parameters are included too.
    /// </summary>
    /// <example>
    /// <code>
    /// fam.GetParameterNamesBySpec(SpecTypeId.Length)
    /// fam.GetParameterNamesBySpec(SpecTypeId.Length, includeTypeParameters: true)
    /// </code>
    /// </example>
    public static List<string> GetParameterNamesBySpec(
        this FamilyInstance familyInstance,
        ForgeTypeId specTypeId,
        bool includeTypeParameters = false)
    {
        IEnumerable<Parameter> parameters = familyInstance.Parameters.Cast<Parameter>();

        if (includeTypeParameters)
            parameters = parameters.Concat(familyInstance.Symbol.Parameters.Cast<Parameter>());

        return parameters
            .Where(p => p.Definition.GetDataType() == specTypeId)
            .Select(p => p.Definition.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Type parameters
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a built-in parameter from the element's <em>type</em> (not the instance).
    /// Returns <c>null</c> when the type element does not exist.
    /// </summary>
    public static Parameter? GetTypeParameter(this Element element, BuiltInParameter builtInParameter)
    {
        Element? type = element.Document.GetElement(element.GetTypeId());
        return type?.get_Parameter(builtInParameter);
    }

    /// <summary>
    /// Looks up a named parameter on the element's <em>type</em> (not the instance).
    /// Returns <c>null</c> when the type element does not exist or the parameter is not found.
    /// </summary>
    public static Parameter? GetTypeParameter(this Element element, string parameterName)
    {
        Element? type = element.Document.GetElement(element.GetTypeId());
        return type?.LookupParameter(parameterName);
    }

    // -------------------------------------------------------------------------
    // Element helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the element referenced by this <see cref="StorageType.ElementId"/> parameter,
    /// cast to <typeparamref name="T"/>.
    /// Returns <c>null</c> when the parameter is <c>null</c>, has no value,
    /// is not an <see cref="StorageType.ElementId"/> parameter,
    /// or the element is not of type <typeparamref name="T"/>.
    /// </summary>
    public static T? AsElement<T>(this Parameter parameter, Document document)
        where T : Element
    {
        if (parameter is null || !parameter.HasValue)
            return null;

        if (parameter.StorageType != StorageType.ElementId)
            return null;

        ElementId id = parameter.AsElementId();
        if (id is null)
            return null;

        return document.GetElement(id) as T;
    }

    // -------------------------------------------------------------------------
    // Boolean helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads a Yes/No parameter as <c>bool</c>.
    /// Returns <c>false</c> when the parameter is <c>null</c>, has no value,
    /// or is not a Yes/No type.
    /// </summary>
    public static bool AsBool(this Parameter parameter)
    {
        if (parameter is null || !parameter.HasValue)
            return false;

#if REVIT_2020
        if (parameter.Definition.ParameterType != ParameterType.YesNo)
            return false;
#else
        if (parameter.Definition.GetDataType() != SpecTypeId.Boolean.YesNo)
            return false;
#endif

        return parameter.AsInteger() == 1;
    }
}
