using System;
using WpfStateService.Dispatching;

namespace TestFramework.DebugUI.Tests.Support;

internal static class StateTestHelpers
{
    static StateTestHelpers()
    {
        StateCommonDispatcher.StateDispatcher = new ImmediateStateDispatcher();
    }

    internal static void EnsureDispatcherInitialized()
    {
    }

    internal static void Eventually(Func<bool> assertion, string failureMessage)
    {
        if (SpinWait.SpinUntil(assertion, TimeSpan.FromSeconds(5)))
            return;

        throw new Xunit.Sdk.XunitException(failureMessage);
    }
}