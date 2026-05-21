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
        if (Thread.CurrentThread == _workerThread)
        {
            action();
            return;
        }

        if (StateCommonDispatcher.StateDispatcher is IStateMutationDispatcher mutationDispatcher)
        {
            mutationDispatcher.DispatchState(action);
            return;
        }

        _actionQueue.Add(action);
    }

    internal static Task DispatchAsync(Action action)
    {
        if (Thread.CurrentThread == _workerThread)
        {
            action();
            return Task.CompletedTask;
        }

        if (StateCommonDispatcher.StateDispatcher is IStateMutationDispatcher mutationDispatcher)
        {
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            mutationDispatcher.DispatchState(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });

            return completion.Task;
        }

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _actionQueue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
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

    internal static Task<T> DispatchAsync<T>(Func<T> func)
    {
        if (Thread.CurrentThread == _workerThread)
            return Task.FromResult(func());

        if (StateCommonDispatcher.StateDispatcher is IStateMutationDispatcher mutationDispatcher)
        {
            TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            mutationDispatcher.DispatchState(() =>
            {
                try
                {
                    completion.SetResult(func());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            });

            return completion.Task;
        }

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _actionQueue.Add(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}