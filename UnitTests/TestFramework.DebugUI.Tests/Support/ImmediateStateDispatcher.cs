using WpfStateService.Dispatching;

namespace TestFramework.DebugUI.Tests.Support;

internal sealed class ImmediateStateDispatcher : IStateMutationDispatcher
{
    private readonly object stateLock = new();

    public Task DispatchCallbackAsync(Func<Task> func)
    {
        return func();
    }

    public void DispatchState(Action action)
    {
        lock (stateLock)
        {
            action();
        }
    }
}