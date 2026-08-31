using System;
using System.Linq;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers projecting the evidence a run produced onto the graph.
/// </summary>
/// <remarks>
/// A run says what happened but not what it looked like, and this is the half that carries the
/// difference: a picture, filed against the step and attempt that produced it, kept in the order the
/// run produced them.
/// </remarks>
public class RunProjectionWidgetTests
{
    [Fact]
    public void AnWidgetKeepsWhereItCameFrom()
    {
        RunGraph graph = RunProjection.ApplyWidget(RunGraph.Empty, Signal("page", stage: "Main", stepId: 2, attempt: 3));

        WidgetNode node = Assert.Single(graph.Widgets);

        Assert.Equal("Main", node.Stage);
        Assert.Equal(2, node.StepId);
        Assert.Equal(3, node.Attempt);
        Assert.Equal(WidgetKinds.Screenshot, node.Kind);
        Assert.Equal("page", node.Name);
    }

    [Fact]
    public void WidgetsStayInTheOrderTheRunProducedThem()
    {
        // What makes a filmstrip a filmstrip: the sequence is the thing being read.
        RunGraph graph = RunGraph.Empty;

        graph = RunProjection.ApplyWidget(graph, Signal("login", at: 1));
        graph = RunProjection.ApplyWidget(graph, Signal("basket", at: 2));
        graph = RunProjection.ApplyWidget(graph, Signal("checkout", at: 3));

        Assert.Equal(["login", "basket", "checkout"], graph.Widgets.Select(widget => widget.Name));
    }

    [Fact]
    public void ReplayingARunDoesNotCollectItsEvidenceTwice()
    {
        // Every reopen of a recorded run replays the whole journal. Appending without this turns one
        // screenshot into one per time the run was opened.
        PipeWidgetSignal signal = Signal("page");

        RunGraph graph = RunProjection.ApplyWidget(RunGraph.Empty, signal);
        graph = RunProjection.ApplyWidget(graph, signal);

        Assert.Single(graph.Widgets);
    }

    [Fact]
    public void TheSamePictureFromADifferentAttemptIsADifferentWidget()
    {
        // The other half of the rule above. A step that retried produced one per attempt, and
        // collapsing them would hide the one that shows what went wrong the first time.
        RunGraph graph = RunProjection.ApplyWidget(RunGraph.Empty, Signal("page", attempt: 1));
        graph = RunProjection.ApplyWidget(graph, Signal("page", attempt: 2));

        Assert.Equal(2, graph.Widgets.Count);
    }

    [Fact]
    public void AnWidgetOfAnUnfamiliarKindIsStillKept()
    {
        // Kinds are strings so a package can add one without Core shipping first. A consumer that
        // does not recognise one still has a description, a summary and a file to offer.
        RunGraph graph = RunProjection.ApplyWidget(RunGraph.Empty, Signal("topology", kind: "tf.widget.topology"));

        Assert.Equal("tf.widget.topology", Assert.Single(graph.Widgets).Kind);
    }

    [Fact]
    public void AnWidgetReachesTheGraphThroughTheSameDispatchEverySignalUses()
    {
        // The application applies signals through one entry point; a kind missing from it is a kind
        // that silently never arrives.
        RunGraph graph = RunProjection.Apply(RunGraph.Empty, Signal("page"));

        Assert.Single(graph.Widgets);
    }

    [Fact]
    public void AStepsOwnEvidenceIsToldApartFromAComponents()
    {
        // A component's evidence is filed against the step that built the environment, so a step
        // panel filtering only by step id would show a container's log on an unrelated step.
        RunGraph graph = RunProjection.ApplyWidget(RunGraph.Empty, Signal("page", stage: "Main", stepId: 0));
        graph = RunProjection.ApplyWidget(graph, Signal("api-log", stage: "Main", stepId: 0, component: "api-container"));

        Assert.Equal(["page"], graph.Widgets.Where(widget => widget.BelongsTo("Main", 0)).Select(widget => widget.Name));
    }

    private static PipeWidgetSignal Signal(
        string name,
        string kind = WidgetKinds.Screenshot,
        string? stage = "Main",
        int? stepId = 0,
        int? attempt = 1,
        string? component = null,
        int at = 0)
        => new()
        {
            SessionId = "s1",
            Entry = new DebugWidgetEntry
            {
                Stage = stage,
                StepId = stepId,
                Attempt = attempt,
                Component = component,
                Kind = kind,
                Name = name,
                OccurredAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(at),
                Description = new DebugValueDescription
                {
                    Summary = name,
                    Preview = new DebugValuePreview { Form = DebugPreviewForm.Image, Text = string.Empty, IsTruncated = true, SizeInBytes = 12 },
                    Body = new DebugValueBody
                    {
                        Path = $@"C:\runs\widgets\{name}.png",
                        RelativePath = $"widgets/{name}.png",
                        SizeInBytes = 12,
                        ContentHash = "ABCD"
                    }
                }
            }
        };
}
