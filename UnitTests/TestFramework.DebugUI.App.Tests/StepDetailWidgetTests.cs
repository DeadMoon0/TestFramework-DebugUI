using System;
using System.Collections.Generic;
using System.Windows;
using Axiom.State;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Debugger;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.DebugUI.Controls.Detail;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.State.Board;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers which widgets the step panel shows as the reader moves from step to step.
/// </summary>
/// <remarks>
/// A widget shown under the wrong step is worse than none: it is evidence, and a reader looking at a
/// failed assertion beside a screenshot will take the screenshot to be what that step saw.
/// </remarks>
public sealed class StepDetailWidgetTests
{
    [Fact]
    public void AStepThatProducedNoWidgetsShowsNoneEvenRightAfterOneThatDid()
    {
        string sessionId = "step-detail-" + Guid.NewGuid().ToString("N");

        Wpf.Run(() =>
        {
            using StateStore<MainState> store = MainStore.Create().Build();

            store.Dispatch(MainActions.SelectRun, sessionId);
            store.Dispatch(MainActions.IngestBatch, [Init(sessionId), Widget(sessionId, stepId: 0)]);

            using UC_StepDetail detail = new(Shells.For(store));

            store.Dispatch(BoardActions.SelectStep, new StepSelection { StageName = "Main", StepId = 0 });
            Assert.Equal(Visibility.Visible, detail.spWidgets.Visibility);

            store.Dispatch(BoardActions.SelectStep, new StepSelection { StageName = "Main", StepId = 1 });

            Assert.Equal(Visibility.Collapsed, detail.spWidgets.Visibility);
            Assert.Empty(detail.spWidgets.Children);
        });
    }

    private static DebugEnvelope Init(string sessionId) => DebugEnvelopeCodec.Wrap(new PipeInitTimelineRunSignal
    {
        SessionId = sessionId,
        Name = "widgets",
        ProjectPath = "project.csproj",
        RunStructure = new TimelineRunStructure
        {
            Stages = [new DebugStageState { Name = "Main", Description = "main", Steps = [Step("look"), Step("check")] }],
            Variables = new Dictionary<VariableIdentifier, DebugValue>(),
            Artifacts = new Dictionary<ArtifactIdentifier, DebugValue>()
        }
    }, 1);

    private static DebugEnvelope Widget(string sessionId, int stepId) => DebugEnvelopeCodec.Wrap(new PipeWidgetSignal
    {
        SessionId = sessionId,
        Entry = new DebugWidgetEntry
        {
            Stage = "Main",
            StepId = stepId,
            Attempt = 1,
            Kind = WidgetKinds.Screenshot,
            Name = "page",
            OccurredAtUtc = DateTimeOffset.UnixEpoch,
            Description = new DebugValueDescription
            {
                Summary = "page",
                Preview = new DebugValuePreview { Form = DebugPreviewForm.Image, Text = string.Empty, IsTruncated = true, SizeInBytes = 12 },
                Body = new DebugValueBody { Path = @"C:\runs\widgets\page.png", RelativePath = "widgets/page.png", SizeInBytes = 12, ContentHash = "ABCD" }
            }
        }
    }, 2);

    private static DebugStepState Step(string name) => new()
    {
        Name = name,
        Description = name,
        DoesReturn = false,
        Phase = StepExecutionPhase.Act,
        Parallelization = StepParallelizationMode.Parallelizable
    };
}
