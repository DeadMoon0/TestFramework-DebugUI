using WpfStateService.Dispatching;

namespace TestFramework.DebugUI.Tests.Support;

internal sealed class ImmediateStateDispatcher : IStateMutationDispatcher
{
    public Task DispatchCallbackAsync(Func<Task> func)
    {
        return func();
    }

    public void DispatchState(Action action)
    {
        action();
    }
}