using Autodesk.Revit.DB;
using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

/// <summary>
/// Tests for <see cref="ElementTransformExtensions"/>.
/// All tests require a live Revit process and an open document with elements.
/// </summary>
public class ElementTransformExtensionsTests
{
    private const string RevitRequired = "Requires a live Revit process with an open document.";

    [Fact(Skip = RevitRequired)]
    public void Move_XyzOverload_ReturnsSameElement()
    {
        // Arrange: get a wall element from an open document (via Revit Test Runner)
        Element element = null!; // replace with real element in runner
        var originalId = element.Id;

        // Act
        var result = element.Move(1.0, 0.0, 0.0);

        // Assert: fluent return is the same element
        result.Id.Should().Be(originalId);
    }

    [Fact(Skip = RevitRequired)]
    public void Move_VectorOverload_ReturnsSameElement()
    {
        Element element = null!;
        var result = element.Move(new XYZ(0, 1, 0));
        result.Id.Should().Be(element.Id);
    }

    [Fact(Skip = RevitRequired)]
    public void Rotate_ReturnsSameElement()
    {
        Element element = null!;
        Line axis = Line.CreateUnbound(XYZ.Zero, XYZ.BasisZ);
        var result = element.Rotate(axis, Math.PI / 4);
        result.Id.Should().Be(element.Id);
    }

    [Fact(Skip = RevitRequired)]
    public void Copy_ReturnsNewElement()
    {
        Element element = null!;
        Element? copy = element.Copy(new XYZ(5, 0, 0));
        copy.Should().NotBeNull();
        copy!.Id.Should().NotBe(element.Id);
    }

    [Fact(Skip = RevitRequired)]
    public void Copy_XyzComponentsOverload_ReturnsNewElement()
    {
        Element element = null!;
        Element? copy = element.Copy(5.0, 0.0, 0.0);
        copy.Should().NotBeNull();
        copy!.Id.Should().NotBe(element.Id);
    }
}
