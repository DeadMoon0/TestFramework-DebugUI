using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.DebugUI.TimelineSamples.Tests;

/// <summary>
/// One run that does as much as possible, as loudly as possible, for as long as it takes to watch.
/// </summary>
/// <remarks>
/// <para>
/// Every other sample here is deliberately small: one thing, done once, so that a failure names the thing that
/// broke. This is the opposite and exists for the opposite reason — to put the window under load. A board with
/// sixty steps in it, several layers running at once, thousands of log lines, values too big to send inline,
/// artifacts gaining versions, retries, a timeout, a suppressed exception, a real failure with a cause chain
/// underneath it, and a check that does not hold.
/// </para>
/// <para>
/// It is <em>not</em> a regression test and it asserts almost nothing. Anything it did assert would be a claim
/// about the framework that one of the small samples already makes better. What it asserts is only that the run
/// got as far as it was meant to, so that a green result means the load was actually applied.
/// </para>
/// <para>
/// Attach the debugger and run it. The pacing is deliberate: steps pause for a second or two so the board is
/// visibly working rather than finished before the window has drawn, which is the only way to see a live run
/// behave.
/// </para>
/// </remarks>
public sealed class StressTimelineTests(ITestOutputHelper outputHelper)
{
    /// <summary>How many items the wide fan-out runs over.</summary>
    /// <remarks>
    /// Enough that the board has to lay out a genuinely wide layer and the pipe has to carry a burst of
    /// concurrent traffic from steps that are all logging at once.
    /// </remarks>
    private const int FanOut = 12;

    /// <summary>How many lines the noisiest step writes.</summary>
    /// <remarks>
    /// Chosen to be past anything a reader would scroll through, because the point is what the step panel does
    /// with a log it cannot show all of — not whether four hundred lines arrive.
    /// </remarks>
    private const int ChattyLines = 1500;

    [Fact]
    public async Task Everything_AtOnce_UnderLoad()
    {
        ConcurrentBag<string> visited = [];
        int flakyAttempts = 0;

        Timeline timeline = Timeline.Create()

            // ---------------------------------------------------------------- values, including awkward ones
            .SetVariable("suite", Var.Const("Checkout"))
            .SetVariable("environment", Var.Const("staging-eu-west-1"))
            .SetVariable("build", Var.Const(20_260_820))
            .SetVariable("budget", Var.Const(3))
            .SetVariable("patience", Var.Const(TimeSpan.FromMilliseconds(400)))
            .SetVariable("orders", Var.Const(Enumerable.Range(1, 40).Select(index => $"ORD-{index:0000}").ToArray()))

            // A value far past what the transport sends inline, so the run has to write it beside the journal
            // and the inspector has to show a preview of a file rather than a value.
            .SetVariable("payload", Var.Const(Payload(240_000)))

            // A value that is one long line rather than a lot of them, which wraps and trims differently.
            .SetVariable("oneLongLine", Var.Const(new string('x', 12_000)))

            .Transform("label", Var.Ref<string>("suite"), suite => $"{suite} under load")
            .Name("name the run")

            // ---------------------------------------------------------------- a wide layer that all logs at once
            .ForEach(Enumerable.Range(1, FanOut).Select(index => $"shard-{index:00}").ToArray(), "shard", loop =>
            {
                loop.Trigger(SimpleExt.Trigger.Action((_, logger, vars, _) =>
                {
                    string shard = (string)vars[new VariableIdentifier("shard")]!;

                    visited.Add(shard);

                    logger.LogInformation("{0} opened against {1}", shard, "staging-eu-west-1");

                    for (int line = 1; line <= 40; line++)
                        logger.LogInformation("{0} row {1} of 40 reconciled, balance {2}", shard, line, line * 37.5m);

                    // Every shard sleeps, so the whole layer is in flight together rather than finishing one at
                    // a time — which is the only way the board shows a layer as actually parallel.
                    Thread.Sleep(1200);

                    logger.LogInformation("{0} closed", shard);
                }, [Var.Ref<string>("shard")]));
            })

            // ---------------------------------------------------------------- the noisy one
            .Trigger(SimpleExt.Trigger.Action((_, logger, _, _) =>
            {
                logger.LogInformation("Replaying {0} events from {1}", ChattyLines, "the write-ahead log");

                for (int line = 1; line <= ChattyLines; line++)
                {
                    if (line % 250 == 0)
                    {
                        logger.LogWarning("checkpoint {0} took {1} ms, above the {2} ms budget", line, 214, 200);
                        Thread.Sleep(120);
                        continue;
                    }

                    if (line % 97 == 0)
                    {
                        // A single entry that is itself many lines, which the panel has to lay out as one row.
                        logger.LogInformation(
                            "event {0} rejected\n  reason: schema mismatch\n  expected: v4\n  received: v3\n  field: totals.netAmount",
                            line);
                        continue;
                    }

                    logger.LogInformation("event {0} applied to {1} in {2} ms", line, "ledger", (line % 17) + 1);
                }

                logger.LogInformation("Replay finished");
            }, []))
            .Name("replay the log")

            // ---------------------------------------------------------------- a step that will not share
            .Trigger(SimpleExt.Trigger.Action((_, logger, _, _) =>
            {
                logger.LogInformation("Taking the exclusive lock");
                Thread.Sleep(1500);
                logger.LogInformation("Releasing it");
            }, []))
            .Name("migrate the schema")
            .DoNotParallelize()

            // ---------------------------------------------------------------- retries
            .Trigger(SimpleExt.Trigger.Action((_, logger, _, _) =>
            {
                flakyAttempts++;

                logger.LogInformation("Attempt {0} against {1}", flakyAttempts, "the flaky gateway");
                Thread.Sleep(500);

                if (flakyAttempts < 3)
                    throw new TimeoutException($"Gateway did not answer on attempt {flakyAttempts}.");

                logger.LogInformation("Gateway answered on attempt {0}", flakyAttempts);
            }, []))
            .Name("call the flaky gateway")
            .WithRetry(Var.Ref<int>("budget"), CalcDelays.None)

            // ---------------------------------------------------------------- a failure that is absorbed
            .Trigger(SimpleExt.Trigger.Action((_, logger, _, _) =>
            {
                logger.LogWarning("The optional cache is not reachable; carrying on without it");
                throw new InvalidOperationException("Optional cache unavailable.");
            }, []))
            .Name("warm the optional cache")
            .ExpectExceptions(typeof(InvalidOperationException))

            // ---------------------------------------------------------------- a step that runs out of time
            .Trigger(SimpleExt.Trigger.Action((_, logger, _, _) =>
            {
                logger.LogInformation("Waiting on a queue that never answers");
                Thread.Sleep(5000);
                logger.LogInformation("This line is never reached");
            }, []))
            .Name("drain the dead queue")
            .WithTimeOut(Var.Ref<TimeSpan>("patience"))
            .ExpectExceptions(typeof(TaskCanceledException), typeof(OperationCanceledException), typeof(TimeoutException))

            // ---------------------------------------------------------------- checks that hold
            .AssertVariable(Var.Ref<string>("environment"), environment => environment == "staging-eu-west-1")
            .AssertVariable(Var.Ref<int>("build"), build => build > 20_260_000)
            .AssertVariable(Var.Ref<string>("label"), label => label!.Contains("under load", StringComparison.Ordinal))
            .AssertVariable(Var.Ref<string[]>("orders"), orders => orders!.Length == 40)

            // ---------------------------------------------------------------- a second wide layer, quieter
            .ForEach(Enumerable.Range(1, 6).Select(index => $"region-{index}").ToArray(), "region", loop =>
            {
                loop.Trigger(SimpleExt.Trigger.Action((_, logger, vars, _) =>
                {
                    string region = (string)vars[new VariableIdentifier("region")]!;

                    logger.LogInformation("{0} settled {1} orders", region, 40);
                    Thread.Sleep(700);
                }, [Var.Ref<string>("region")]));
            })

            // ---------------------------------------------------------------- the one that really breaks
            .Trigger(SimpleExt.Trigger.Action((_, logger, _, _) =>
            {
                logger.LogError("Posting the batch to the ledger");
                Thread.Sleep(400);

                throw new InvalidOperationException(
                    "The ledger refused the batch.",
                    new AggregateException(
                        "One or more entries were rejected.",
                        new FormatException("Entry 17: total '1.234,56' is not a decimal in this culture."),
                        new OverflowException("Entry 22: 9223372036854775808 does not fit in an Int64.")));
            }, []))
            .Name("post the batch")

            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        // The only claims worth making: the load was applied, and the run got where it was going. Anything more
        // would be a statement about the framework that a smaller sample already makes more clearly.
        Assert.Equal(FanOut, visited.Distinct().Count());
        Assert.Equal(3, flakyAttempts);
        // The last step is meant to break, so that the window has a failure to show. Asserted as the throw the
        // framework actually makes rather than a flag, which is how the other failing sample here reads too.
        Assert.Throws<TestFramework.Core.Exceptions.TimelineRunFailedException>(run.EnsureRanToCompletion);
    }

    /// <summary>
    /// A value big enough that the run writes it to a file instead of putting it on the wire.
    /// </summary>
    /// <remarks>
    /// Shaped like something rather than filled with one character: the inspector shows a preview, and a preview
    /// of two hundred thousand identical bytes tells you nothing about whether the preview works.
    /// </remarks>
    private static string Payload(int size)
    {
        StringBuilder text = new(size + 64);
        int order = 0;

        while (text.Length < size)
        {
            order++;

            text.Append(CultureInfo.InvariantCulture, $"{{\"order\":\"ORD-{order:0000}\",");
            text.Append(CultureInfo.InvariantCulture, $"\"customer\":\"customer-{order % 97:000}\",");
            text.Append(CultureInfo.InvariantCulture, $"\"net\":{(order * 37.5m) % 9999:0.00},");
            text.Append(CultureInfo.InvariantCulture, $"\"lines\":{(order % 7) + 1},");
            text.Append(CultureInfo.InvariantCulture, $"\"note\":\"{new string('.', order % 40)}\"}}");
            text.AppendLine(",");
        }

        return text.ToString();
    }
}
