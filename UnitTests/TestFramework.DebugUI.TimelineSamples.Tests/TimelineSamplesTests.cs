using System.Collections.Concurrent;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.LocalIO;
using TestFramework.Simple;
using Xunit.Abstractions;

namespace TestFramework.DebugUI.TimelineSamples.Tests;

file static class TimelineSamplePaths
{
    public static string BuildOutput => AppContext.BaseDirectory;

    public static string UniqueFile(string prefix)
        => Path.Combine(BuildOutput, $"{prefix}-{Guid.NewGuid():N}.txt");
}

file static class TimelineSampleShell
{
    public static string WriteText(string fileName, string text)
        => $"echo {text} > \"{fileName}\"";

    public static string AppendText(string fileName, string text)
        => $"echo {text} >> \"{fileName}\"";

    /// <summary>A command that does nothing for a given number of seconds.</summary>
    /// <remarks>
    /// Ping rather than timeout: <c>timeout</c> refuses to run when its input is redirected, which is
    /// exactly how a step runs it.
    /// </remarks>
    public static string Pause(int seconds)
        => OperatingSystem.IsWindows()
            ? $"ping 127.0.0.1 -n {seconds + 1}"
            : $"sleep {seconds}";

    public static string DelayedWriteText(string fileName, string text)
        => OperatingSystem.IsWindows()
            ? $"ping 127.0.0.1 -n 2 > nul & {WriteText(fileName, text)}"
            : $"sleep 1; {WriteText(fileName, text)}";
}

file static class TimelineSampleAssertions
{
    public static string Normalize(string value)
    => string.Join("\n", value.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()));
}

public sealed class TimelineSamplesTests(ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task MinimalTimeline_Completes()
    {
        Timeline timeline = Timeline.Create()
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
    }

    [Fact]
    public async Task VariablesAndAssertions_Completes()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("name", Var.Const("Ada"))
            .Transform("greeting", Var.Ref<string>("name"), name => $"Hello {name}")
            .AssertVariable(Var.Ref<string>("greeting"), greeting => greeting == "Hello Ada")
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal("Hello Ada", run.VariableStore.GetVariable<string>("greeting"));
    }

    /// <summary>
    /// A run whose assertion does not hold, which is the case the debugger exists for.
    /// </summary>
    /// <remarks>
    /// Every other sample passes, so without this one nothing here ever drives the UI's failure
    /// path: no red verdict, no failed step, no failure detail in the panel. The test passes by the
    /// run failing.
    /// </remarks>
    [Fact]
    public async Task AssertionThatDoesNotHold_FailsTheRun()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("name", Var.Const("Grace"))
            .AssertVariable(Var.Ref<string>("name"), name => name == "Ada")
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
    }

    /// <summary>
    /// A run whose values are too large to send, which is the case the value files exist for.
    /// </summary>
    /// <remarks>
    /// Every other sample assigns something that fits in a preview, so without this one nothing here
    /// ever drives the path where a value is written to the run's output and the UI shows a
    /// reference to it instead of the content.
    /// </remarks>
    [Fact]
    public async Task LargeValues_AreWrittenToTheRunOutput()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("orderIds", Var.Const(Enumerable.Range(1, 4000).ToArray()))
            .Transform("report", Var.Ref<int[]>("orderIds"), ids => string.Join(Environment.NewLine, (ids ?? []).Select(id => $"order {id} accepted")))
            .SetVariable("shortNote", Var.Const("small enough to send"))

            // Bytes as well as text, so the binary path is exercised too: the preview arrives as hex
            // and the whole value is written as a .bin someone can open with whatever reads it.
            .SetVariable("screenshot", Var.Const(SampleBytes(9000)))
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(4000, run.VariableStore.GetVariable<int[]>("orderIds")!.Length);
        Assert.Equal(9000, run.VariableStore.GetVariable<byte[]>("screenshot")!.Length);
    }

    /// <summary>
    /// Bytes that begin with a PNG header and then carry readable text.
    /// </summary>
    /// <remarks>
    /// Deliberately not random. A dump of random bytes shows nothing about whether the dump works;
    /// these have a recognisable head, a run of unprintable bytes, and text in the middle, so all
    /// three columns can be judged at a glance.
    /// </remarks>
    private static byte[] SampleBytes(int length)
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] bytes = new byte[length];

        header.CopyTo(bytes, 0);

        for (int index = header.Length; index < length; index++)
            bytes[index] = (byte)(index % 96 == 0 ? 0 : 'a' + (index % 26));

        return bytes;
    }

    /// <summary>
    /// A run slow enough to be watched while it happens.
    /// </summary>
    /// <remarks>
    /// Every other sample finishes in a couple of hundred milliseconds, so the board only ever shows
    /// them already over. This one takes several seconds and moves through its steps one at a time,
    /// which is the only way to see that a step reads as running while it runs, that the ones after
    /// it read as not yet run, and that the board keeps up live rather than arriving complete.
    /// </remarks>
    [Fact]
    public async Task SlowSequence_CanBeWatchedWhileItRuns()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("started", Var.Const("yes"))
            .SetVariable("pause", Var.Const(TimelineSampleShell.Pause(2)))
            .SetVariable("cwd", Var.Const(TimelineSamplePaths.BuildOutput))
            .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("pause"), Var.Ref<string>("cwd")))
            .Transform("firstDone", Var.Ref<string>("started"), started => $"{started}-first")
            .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("pause"), Var.Ref<string>("cwd")))
            .Transform("secondDone", Var.Ref<string>("firstDone"), first => $"{first}-second")
            .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("pause"), Var.Ref<string>("cwd")))
            .AssertVariable(Var.Ref<string>("secondDone"), value => value == "yes-first-second")
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal("yes-first-second", run.VariableStore.GetVariable<string>("secondDone"));
    }

    [Fact]
    public async Task SetupArtifactInput_Completes()
    {
        string artifactPath = TimelineSamplePaths.UniqueFile("debugui-sample-seed");

        try
        {
            Timeline timeline = Timeline.Create()
                .SetupArtifact("seed-file")
                .Build();

            TimelineRun run = await timeline.SetupRun(outputHelper)
                .AddFileArtifact("seed-file", artifactPath, "seed-data")
                .RunAsync();

            run.EnsureRanToCompletion();
            Assert.Equal("seed-data", TimelineSampleAssertions.Normalize(run.ArtifactStore.GetFileArtifact("seed-file").Last.DataAsUtf8String));
        }
        finally
        {
            if (File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }
        }
    }

    [Fact]
    public async Task LocalIoEventAndArtifact_Completes()
    {
        string artifactPath = TimelineSamplePaths.UniqueFile("debugui-sample-event");
        string artifactFileName = Path.GetFileName(artifactPath);

        try
        {
            Timeline timeline = Timeline.Create()
                .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdCreate"), Var.Ref<string>("cwd")))
                .WaitForEvent(LocalIOExt.Events.FileExists(Var.Ref<string>("artifactPath")))
                .WithTimeOut(TimeSpan.FromSeconds(10))
                .RegisterArtifact("event-file", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
                .Build();

            TimelineRun run = await timeline.SetupRun(outputHelper)
                .AddVariable("cmdCreate", TimelineSampleShell.DelayedWriteText(artifactFileName, "event-ready"))
                .AddVariable("cwd", TimelineSamplePaths.BuildOutput)
                .AddVariable("artifactPath", artifactPath)
                .RunAsync();

            run.EnsureRanToCompletion();
            Assert.Equal("event-ready", TimelineSampleAssertions.Normalize(run.ArtifactStore.GetFileArtifact("event-file").Last.DataAsUtf8String));
        }
        finally
        {
            if (File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }
        }
    }

    [Fact]
    public async Task RetrySample_CreatesMultipleAttempts()
    {
        int attempts = 0;

        Timeline timeline = Timeline.Create()
            .Trigger(SimpleExt.Trigger.Action(() =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("Planned sample failure.");
                }
            }))
            .Name("flaky")
            .WithRetry(2, CalcDelays.None)
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(3, attempts);
        Assert.Equal(3, run.Step("flaky").RetryResults.Count);
    }

    [Fact]
    public async Task ForEachFanOut_Completes()
    {
        ConcurrentBag<string> seen = [];

        Timeline timeline = Timeline.Create()
            .ForEach(new[] { "alpha", "beta", "gamma" }, "item", loop =>
            {
                loop.Trigger(SimpleExt.Trigger.Action(vars =>
                {
                    string value = (string)vars[new VariableIdentifier("item")]!;
                    seen.Add(value);
                }, Var.Ref<string>("item")));
            })
            .Build();

        TimelineRun run = await timeline.SetupRun(outputHelper).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(3, seen.Count);
        Assert.Contains("alpha", seen);
        Assert.Contains("beta", seen);
        Assert.Contains("gamma", seen);
    }

    [Fact]
    public async Task ArtifactVersionJourney_Completes()
    {
        string artifactPath = TimelineSamplePaths.UniqueFile("debugui-sample-version");
        string artifactFileName = Path.GetFileName(artifactPath);

        try
        {
            Timeline timeline = Timeline.Create()
                .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdAppendFirst"), Var.Ref<string>("cwd")))
                .RegisterArtifact("log-file", LocalIOExt.Artifacts.FileRef(Var.Ref<string>("artifactPath")))
                .Trigger(LocalIOExt.Trigger.Cmd(Var.Ref<string>("cmdAppendSecond"), Var.Ref<string>("cwd")))
                .CaptureArtifactVersion("log-file", "after-second-write")
                .Build();

            TimelineRun run = await timeline.SetupRun(outputHelper)
                .AddVariable("cmdAppendFirst", TimelineSampleShell.AppendText(artifactFileName, "line-one"))
                .AddVariable("cmdAppendSecond", TimelineSampleShell.AppendText(artifactFileName, "line-two"))
                .AddVariable("cwd", TimelineSamplePaths.BuildOutput)
                .AddVariable("artifactPath", artifactPath)
                .RunAsync();

            run.EnsureRanToCompletion();
            Assert.Equal("line-one", TimelineSampleAssertions.Normalize(run.ArtifactStore.GetFileArtifact("log-file").First.DataAsUtf8String));
            Assert.Equal("line-one\nline-two", TimelineSampleAssertions.Normalize(run.ArtifactStore.GetFileArtifact("log-file")["after-second-write"].DataAsUtf8String));
        }
        finally
        {
            if (File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }
        }
    }
}