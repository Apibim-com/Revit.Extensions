using FluentAssertions;
using Xunit;

namespace Revit.Extensions.Tests;

public class AsyncTasksExecutorTests
{
    [Fact]
    public void Execute_WithTaskFunc_ReturnsCorrectResult()
    {
        var result = AsyncTasksExecutor.Execute<int>(() => Task.FromResult(42));

        result.Should().Be(42);
    }

    [Fact]
    public void Execute_WithAsyncTaskFunc_ReturnsCorrectResult()
    {
        Func<Task<string>> action = async () =>
        {
            await Task.Delay(1);
            return "hello";
        };

        var result = AsyncTasksExecutor.Execute(action);

        result.Should().Be("hello");
    }

    [Fact]
    public void Execute_WithValueTaskFunc_ReturnsCorrectResult()
    {
        var result = AsyncTasksExecutor.Execute<int>(() => new ValueTask<int>(99));

        result.Should().Be(99);
    }

    [Fact]
    public void Execute_WithAsyncValueTaskFunc_ReturnsCorrectResult()
    {
        Func<ValueTask<double>> action = async () =>
        {
            await Task.Delay(1);
            return 3.14;
        };

        var result = AsyncTasksExecutor.Execute(action);

        result.Should().BeApproximately(3.14, 1e-9);
    }
}
