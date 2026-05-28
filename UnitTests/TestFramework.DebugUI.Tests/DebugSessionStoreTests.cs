using TestFramework.Core.Debugger;
using TestFramework.DebugUI.PipeAdapter;

namespace TestFramework.DebugUI.Tests;

public sealed class DebugSessionStoreTests
{
    [Fact]
    public void Store_RetainsOnlyMostRecentFinishedSessions()
    {
        DebugSessionStore store = new();

        for (int index = 0; index < 25; index++)
        {
            string sessionId = $"session-{index:D2}";
            store.Append(new InitTimelineRunSignal
            {
                SessionId = sessionId,
                Name = $"Run {index}",
                ProjectPath = $"project-{index}.csproj",
                RunStructure = new TimelineRunStructure
                {
                    Variables = new Dictionary<TestFramework.Core.Variables.VariableIdentifier, VariableState>(),
                    Artifacts = new Dictionary<TestFramework.Core.Artifacts.ArtifactIdentifier, ArtifactState>(),
                    Stages = []
                }
            });
            store.Append(new TimelineRunFinishedSignal
            {
                SessionId = sessionId
            });
        }

        IReadOnlyList<StoredDebugSessionInfo> sessions = store.ListSessions();

        Assert.Equal(20, sessions.Count);
        Assert.DoesNotContain(sessions, session => session.SessionId == "session-00");
        Assert.DoesNotContain(sessions, session => session.SessionId == "session-04");
        Assert.Contains(sessions, session => session.SessionId == "session-24");
    }
}