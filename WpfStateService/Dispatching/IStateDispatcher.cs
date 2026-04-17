namespace WpfStateService.Dispatching;

public interface IStateDispatcher
{
    public Task DispatchCallbackAsync(Func<Task> func);
}