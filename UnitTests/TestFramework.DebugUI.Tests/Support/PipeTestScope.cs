namespace TestFramework.DebugUI.Tests.Support;

internal sealed class PipeTestScope : IDisposable
{
    private const string PipeNameVariable = "TESTFRAMEWORK_DEBUG_PIPE_NAME";
    private readonly string? previousPipeName;

    private PipeTestScope(string pipeName)
    {
        PipeName = pipeName;
        previousPipeName = Environment.GetEnvironmentVariable(PipeNameVariable);
        Environment.SetEnvironmentVariable(PipeNameVariable, pipeName);
    }

    internal string PipeName { get; }

    internal static PipeTestScope Create()
    {
        string pipeName = $"TestFrameworkDebugTests_{Guid.NewGuid():N}";
        return new PipeTestScope(pipeName);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PipeNameVariable, previousPipeName);
    }
}