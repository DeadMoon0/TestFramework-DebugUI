using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers projecting variable and artifact updates onto the run graph.
/// </summary>
public class RunProjectionValueTests
{
    [Fact]
    public void AVariableUpdateRecordsItsSummaryAndRendererKey()
    {
        RunGraph graph = RunProjection.ApplyValueUpdate(Empty(), Variable("orderId", "42", "tf.variable:System.Int32"));

        ValueNode value = graph.Variables["orderId"];
        Assert.Equal("42", value.Description.Summary);
        Assert.Equal("tf.variable:System.Int32", value.SchemaKey);
    }

    [Fact]
    public void WritingAVariableAgainReplacesItRatherThanAccumulating()
    {
        RunGraph graph = Empty();
        graph = RunProjection.ApplyValueUpdate(graph, Variable("orderId", "42", "tf.variable:System.Int32"));
        graph = RunProjection.ApplyValueUpdate(graph, Variable("orderId", "43", "tf.variable:System.Int32"));

        Assert.Single(graph.Variables);
        Assert.Equal("43", graph.Variables["orderId"].Description.Summary);
    }

    [Fact]
    public void AnArtifactUpdateCarriesItsWholeVersionHistory()
    {
        // Core reports the full history on every update, which is what lets a consumer that attached
        // late still draw v1 -> v2 -> v3 instead of only the versions it happened to witness.
        RunGraph graph = RunProjection.ApplyValueUpdate(
            Empty(),
            Artifact("receipt", "tf.artifact.sql.row", state: "Setup", versions: ["", "v2", "v3"]));

        ArtifactNode artifact = graph.Artifacts["receipt"];
        Assert.Equal(["", "v2", "v3"], artifact.Versions);
        Assert.Equal("Setup", artifact.State);
        Assert.Equal("tf.artifact.sql.row", artifact.SchemaKey);
    }

    [Fact]
    public void ArtifactLifecycleComesFromTheUpdateNotATransition()
    {
        // An artifact moves NotSetup -> Setup -> Cleaned by being mutated in place, which Core
        // reports as a value update rather than an entity transition.
        RunGraph graph = Empty();
        graph = RunProjection.ApplyValueUpdate(graph, Artifact("receipt", "tf.artifact.sql.row", state: "Setup", versions: [""]));
        graph = RunProjection.ApplyValueUpdate(graph, Artifact("receipt", "tf.artifact.sql.row", state: "Cleaned", versions: [""]));

        Assert.Equal("Cleaned", graph.Artifacts["receipt"].State);
    }

    [Fact]
    public void AnUpdateWithoutHistoryLeavesTheKnownVersionsAlone()
    {
        // A payload that omits the versions must not erase what is already known.
        RunGraph graph = Empty();
        graph = RunProjection.ApplyValueUpdate(graph, Artifact("receipt", "tf.artifact.sql.row", state: "Setup", versions: ["", "v2"]));

        PipeValueUpdateSignal sparse = new()
        {
            SessionId = "session-1",
            Name = "receipt",
            ValueKind = DebugValueKind.Artifact,
            Envelope = new DebugValueEnvelope
            {
                Kind = DebugValueKind.Artifact,
                TypeName = "SqlRow",
                Description = new DebugValueDescription { Summary = "receipt row" },
                SchemaKey = "tf.artifact.sql.row"
            }
        };

        graph = RunProjection.ApplyValueUpdate(graph, sparse);

        Assert.Equal(["", "v2"], graph.Artifacts["receipt"].Versions);
        Assert.Equal("Setup", graph.Artifacts["receipt"].State);
    }

    [Fact]
    public void ApplyingTheSameUpdateTwiceConverges()
    {
        PipeValueUpdateSignal signal = Artifact("receipt", "tf.artifact.sql.row", state: "Setup", versions: ["", "v2"]);

        RunGraph once = RunProjection.ApplyValueUpdate(Empty(), signal);
        RunGraph twice = RunProjection.ApplyValueUpdate(once, signal);

        Assert.Equal(once.Artifacts["receipt"], twice.Artifacts["receipt"]);
        Assert.Single(twice.Artifacts);
    }

    [Fact]
    public void VariablesAndArtifactsDoNotShareAKeyspace()
    {
        RunGraph graph = Empty();
        graph = RunProjection.ApplyValueUpdate(graph, Variable("shared", "value", "tf.variable:System.String"));
        graph = RunProjection.ApplyValueUpdate(graph, Artifact("shared", "tf.artifact.file", state: "Setup", versions: [""]));

        Assert.Single(graph.Variables);
        Assert.Single(graph.Artifacts);
        Assert.Equal("value", graph.Variables["shared"].Description.Summary);
    }

    private static RunGraph Empty() => new();

    private static PipeValueUpdateSignal Variable(string name, string display, string schemaKey) => new()
    {
        SessionId = "session-1",
        Name = name,
        ValueKind = DebugValueKind.Variable,
        Envelope = new DebugValueEnvelope
        {
            Kind = DebugValueKind.Variable,
            TypeName = "System.String",
            Description = new DebugValueDescription { Summary = display },
            SchemaKey = schemaKey
        }
    };

    private static PipeValueUpdateSignal Artifact(string name, string schemaKey, string state, string[] versions) => new()
    {
        SessionId = "session-1",
        Name = name,
        ValueKind = DebugValueKind.Artifact,
        Envelope = new DebugValueEnvelope
        {
            Kind = DebugValueKind.Artifact,
            TypeName = "SqlRow",
            Description = new DebugValueDescription { Summary = name + " row" },
            SchemaKey = schemaKey,
            Lifecycle = new DebugValueLifecycle
            {
                State = state,
                Versions = versions,
                CurrentVersion = versions.Length == 0 ? null : versions[^1]
            }
        }
    };
}
