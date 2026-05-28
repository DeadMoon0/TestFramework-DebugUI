using TestFramework.DebugUI.PipeAdapter;
using TestFrameworkDebugUI;

namespace TestFramework.DebugUI.Tests.Support;

internal sealed class DebugStoreScope : IDisposable
{
    private DebugStoreScope()
    {
        DebugSessionService.ResetSharedStoreForTests();
    }

    internal static DebugStoreScope Create()
    {
        return new DebugStoreScope();
    }

    public void Dispose()
    {
        DebugSessionService.ResetSharedStoreForTests();
    }
}