using System.Threading.Tasks;

namespace Revit.Extensions;

/// <summary>
/// Helpers for executing asynchronous work synchronously inside the Revit event loop,
/// where <c>async/await</c> on the main thread is not supported.
/// </summary>
public static class AsyncTasksExecutor
{
    /// <summary>
    /// Executes an async <see cref="Func{Task{T}}"/> synchronously and returns its result.
    /// </summary>
    public static T Execute<T>(Func<Task<T>> action)
    {
        var task = Task.Run(action.Invoke);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Executes an async <see cref="Func{ValueTask{T}}"/> synchronously and returns its result.
    /// </summary>
    public static T Execute<T>(Func<ValueTask<T>> action)
    {
        var task = Task.Run(async () => await action());
        return task.GetAwaiter().GetResult();
    }
}
