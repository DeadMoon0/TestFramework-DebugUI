using System.Collections.Concurrent;
using WpfStateService.Dispatching;
using WpfStateService.Callbacks;
using WpfStateService.Graph;

namespace WpfStateService;

internal static class StateServiceDispatcher
{
    private static readonly BlockingCollection<Action> _actionQueue = [];
    private static readonly BlockingCollection<CallbackChangeMessage> _callbackQueue = [];
    private static readonly Thread _workerThread;
    private static readonly Thread _callbackThread;
    private static readonly TimeSpan _cleanUpTimeout = TimeSpan.FromMinutes(5);

    private static DateTimeOffset _nextCleanup = DateTimeOffset.UtcNow + _cleanUpTimeout;

    static StateServiceDispatcher()
    {
        _workerThread = new Thread(ProcessActions)
        {
            IsBackground = true,
            Name = "StateServiceThread_Actions"
        };
        _callbackThread = new Thread(ProcessCallbacks)
        {
            IsBackground = true,
            Name = "StateServiceThread_Callbacks"
        };
        _workerThread.Start();
        _callbackThread.Start();
    }

    private static void ProcessActions()
    {
        foreach (var action in _actionQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
                if (_nextCleanup <= DateTimeOffset.UtcNow)
                {
                    GraphStore.RunGC();
                    _nextCleanup = DateTimeOffset.UtcNow + _cleanUpTimeout;
                }
            }
            catch
            {
            }
        }
    }

    private static void ProcessCallbacks()
    {
        foreach (var callbackMessage in _callbackQueue.GetConsumingEnumerable())
        {
            try
            {
                callbackMessage.Call();
            }
            catch
            {
            }
        }
    }

    internal static void Dispatch(Action action)
    {
        if (StateCommonDispatcher.StateDispatcher is IStateMutationDispatcher mutationDispatcher)
        {
            mutationDispatcher.DispatchState(action);
            return;
        }

        _actionQueue.Add(action);
    }

    internal static void DispatchCallback(CallbackChangeMessage message)
    {
        if (StateCommonDispatcher.StateDispatcher is IStateMutationDispatcher)
        {
            message.Call();
            return;
        }

        _callbackQueue.Add(message);
    }

    //internal static Task<T> DispatchAsync<T>(Func<T> func)
    //{
    //    var tcs = new TaskCompletionSource<T>();

    //    Dispatch(() =>
    //    {
    //        try
    //        {
    //            var result = func();
    //            tcs.SetResult(result);
    //        }
    //        catch (Exception ex)
    //        {
    //            tcs.SetException(ex);
    //        }
    //    });

    //    return tcs.Task;
    //}
}