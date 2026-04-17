using WpfStateService.Dispatching;

namespace WpfStateService.Callbacks;

internal record CallbackChangeMessage(StateCallbackGeneric[] Actions, object? Value, object? OldValue)
{
    internal void Call()
    {
        Task.WhenAll(Actions.Select(x => StateCommonDispatcher.StateDispatcher.DispatchCallbackAsync(() => x(Value, OldValue)))).Wait();
    }
}