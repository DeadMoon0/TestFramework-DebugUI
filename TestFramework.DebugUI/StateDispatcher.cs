using TestFrameworkDebugUI;
using WpfStateService.Dispatching;

namespace TestFramework.DebugUI;

internal class StateDispatcher : IStateDispatcher
{
    public async Task DispatchCallbackAsync(Func<Task> func)
    {
        await MainWindow.Instance!.Dispatcher.InvokeAsync(func);
    }
}