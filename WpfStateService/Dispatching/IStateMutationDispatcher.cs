namespace WpfStateService.Dispatching;

public interface IStateMutationDispatcher : IStateDispatcher
{
    void DispatchState(Action action);
}