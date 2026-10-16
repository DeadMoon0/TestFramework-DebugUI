using System;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Runs;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Transport;

// Answering a step that asks whether to stop, from what the run has announced about itself.
public sealed partial class ShellController
{
    /// <summary>
    /// Answers a waiting step, naming the test it belongs to.
    /// </summary>
    /// <remarks>
    /// The test comes from <see cref="tests"/> rather than from the store. Both are filled from the same
    /// announcement, but the store's copy arrives through a coalescing dispatch — so for the first step of a
    /// run the store may not have it yet, and a mark on that step would be missed exactly once per run.
    /// </remarks>
    private bool ShouldPause(PipeBreakpointHitRequestSignal request)
    {
        Func<BreakpointQuestion, bool>? test = PauseAtBreakpoint;

        if (test is null)
            return false;

        return test(new BreakpointQuestion
        {
            Request = request,
            Test = tests.TryGetValue(request.SessionId, out string? named) ? named : null
        });
    }

    /// <summary>
    /// Notes what test a run is an execution of, and tells anyone waiting when one of its steps breaks.
    /// </summary>
    /// <remarks>
    /// Read straight off the envelope stream, in order and on the reader thread, which is what makes both
    /// answers available before the next step of that run asks anything.
    /// </remarks>
    private void WatchForBreakpointContext(DebugEnvelope envelope)
    {
        try
        {
            switch (envelope.Kind)
            {
                case PipeSignalKind.InitTimelineRun:
                    if (DebugEnvelopeCodec.Unwrap(envelope) is PipeInitTimelineRunSignal init)
                    {
                        // The fully qualified name when the run could identify itself, and its display name
                        // otherwise — the same fallback the picker shows, so a mark is filed under the name
                        // the reader saw when they set it.
                        tests[envelope.SessionId] = RunSummary.TestNameOf(init.Identity?.FullyQualifiedName, init.Name);
                    }

                    break;

                case PipeSignalKind.TimelineRunFinished:
                    // The last message a session ever sends, so nothing will ask about this run again. Left
                    // in place it would accumulate for the life of the process, which under watch mode is
                    // measured in days and thousands of runs.
                    tests.TryRemove(envelope.SessionId, out _);
                    break;

                // A step about to be retried transitions to WaitingForRetry rather than to Error, so
                // matching on the state is what excludes a failure the run is going to recover from.
                case PipeSignalKind.EntityTransition when StepEndedBadly is not null:
                    if (DebugEnvelopeCodec.Unwrap(envelope) is PipeEntityTransitionSignal
                        {
                            EntityKind: DebugEntityKind.Step,
                            State: DebugLifecycleState.Error or DebugLifecycleState.Timeout,
                            Stage: { Length: > 0 } stage,
                            StepId: int stepId
                        })
                    {
                        StepEndedBadly(new StepFailureNotice
                        {
                            SessionId = envelope.SessionId,
                            Stage = stage,
                            StepId = stepId
                        });
                    }

                    break;

                default:
                    break;
            }
        }
        catch (Exception e)
        {
            // A frame this build cannot read must not take the transport down with it. Losing the test name
            // costs the run its breakpoints; throwing here would cost it the whole connection.
            Log.Write(e);
        }
    }
}
