using System;
using WpfStateService.Dispatching;

namespace TestFramework.DebugUI.Tests.Support;

internal static class StateTestHelpers
{
    static StateTestHelpers()
    {
        EnsureDispatcherInitialized();
    }

    internal static void EnsureDispatcherInitialized()
    {
        StateCommonDispatcher.StateDispatcher = new ImmediateStateDispatcher();
    }

    internal static void Eventually(Func<bool> assertion, string failureMessage)
    {
        if (SpinWait.SpinUntil(assertion, TimeSpan.FromSeconds(5)))
            return;

        throw new Xunit.Sdk.XunitException(failureMessage);
    }

    internal static async Task WithQueuedDispatcherAsync(Func<Task> action)
    {
        IStateDispatcher previousDispatcher = StateCommonDispatcher.StateDispatcher;
        StateCommonDispatcher.StateDispatcher = null!;

        try
        {
            await action();
        }
        finally
        {
            StateCommonDispatcher.StateDispatcher = previousDispatcher;
        }
    }
}