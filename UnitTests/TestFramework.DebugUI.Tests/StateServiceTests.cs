using System.Threading.Tasks;
using System.Collections.Concurrent;
using TestFramework.DebugUI.Tests.Support;
using WpfStateService;
using WpfStateService.Callbacks;
using WpfStateService.Common;
using WpfStateService.Graph;
using WpfStateService.StateServiceObject;

namespace TestFramework.DebugUI.Tests;

public class StateServiceTests
{
    public StateServiceTests()
    {
        StateTestHelpers.EnsureDispatcherInitialized();
    }

    [Fact]
    public void StatePath_CanReadAndWriteNestedValues()
    {
        TestRootState root = new TestRootState();
        TestChildState child = new TestChildState();
        root.Child = child;

        StateTestHelpers.Eventually(() => StatePath.For(root).Property(TestRootState.ChildProperty).GetValue() is not null, "Expected child state to be reachable.");

        bool setResult = StatePath.For(root).Property(TestRootState.ChildProperty).Property(TestChildState.NameProperty).SetValue("updated");

        Assert.True(setResult);
        StateTestHelpers.Eventually(() => root.Child.Name == "updated", "Expected nested property to be updated.");
    }

    [Fact]
    public async Task Callback_OnDirectValueChange_IsTriggered()
    {
        TestRootState root = new TestRootState();
        TaskCompletionSource<string> callback = new();

        StatePath.For(root)
            .Property(TestRootState.TitleProperty)
            .CallbackAsync((value, oldValue) =>
            {
                callback.TrySetResult(value);
                return Task.CompletedTask;
            }, CallbackFlags.OnValueDiffers, triggerWithCurrent: false);

        root.Title = "changed";

        Assert.Equal("changed", await callback.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Callback_OnChildChange_IsTriggeredForParentPath()
    {
        TestRootState root = new TestRootState();
        root.Child = new TestChildState();
        TaskCompletionSource<string> callback = new();

        StatePath.For(root)
            .Property(TestRootState.ChildProperty)
            .CallbackAsync((value, oldValue) =>
            {
                callback.TrySetResult(value.Name);
                return Task.CompletedTask;
            }, CallbackFlags.OnChildChange | CallbackFlags.OnNotNull, triggerWithCurrent: false);

        root.Child.Name = "nested";

        Assert.Equal("nested", await callback.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ReplacingNestedState_UpdatesGraphPath()
    {
        TestRootState root = new TestRootState();
        TestChildState first = new TestChildState { Name = "first" };
        TestChildState second = new TestChildState { Name = "second" };
        root.Child = first;

        StateTestHelpers.Eventually(() => StatePath.For(root).Property(TestRootState.ChildProperty).Property(TestChildState.NameProperty).GetValue() == "first", "Expected first child value.");

        root.Child = second;

        StateTestHelpers.Eventually(() => StatePath.For(root).Property(TestRootState.ChildProperty).Property(TestChildState.NameProperty).GetValue() == "second", "Expected replaced child value.");
    }

    [Fact]
    public void RemovingDictionaryEntry_RemovesPathValue()
    {
        TestRootState root = new TestRootState();
        root.Children.Add("child", new TestChildState { Name = "value" });

        StateTestHelpers.Eventually(() => StatePath.For(root).Property(TestRootState.ChildrenProperty).PropertyKey<TestChildState>("child").GetValue() is not null, "Expected dictionary child to exist.");

        bool removed = root.Children.Remove("child");

        Assert.True(removed);
        StateTestHelpers.Eventually(() => StatePath.For(root).Property(TestRootState.ChildrenProperty).PropertyKey<TestChildState>("child").GetValue() is null, "Expected dictionary child path to be removed.");
    }

    [Fact]
    public async Task Callback_OnNestedPath_RebindsWhenIntermediateStateIsReplaced()
    {
        TestRunHolderState root = new();
        TestRunState first = new();
        first.Stages["stage-1"] = new TestChildState { Name = "first" };
        TestRunState second = new();
        second.Stages["stage-2"] = new TestChildState { Name = "second" };
        root.Run = first;

        TaskCompletionSource<(string NewKey, string OldKey)> replacementObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string[]> childChangeObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<string> snapshots = [];

        StatePath.For(root)
            .Property(TestRunHolderState.RunProperty)
            .Property(TestRunState.StagesProperty)
            .CallbackAsync((value, oldValue) =>
            {
                string newKeys = value is null ? "<null>" : string.Join(",", value.Keys.OrderBy(x => x, StringComparer.Ordinal));
                string oldKeys = oldValue is null ? "<null>" : string.Join(",", oldValue.Keys.OrderBy(x => x, StringComparer.Ordinal));
                snapshots.Enqueue($"new:{newKeys}|old:{oldKeys}");

                if (value is not null && oldValue is not null && value.ContainsKey("stage-2") && oldValue.ContainsKey("stage-1"))
                    replacementObserved.TrySetResult(("stage-2", "stage-1"));

                if (value is not null && value.ContainsKey("stage-3"))
                    childChangeObserved.TrySetResult([.. value.Keys.OrderBy(x => x, StringComparer.Ordinal)]);

                return Task.CompletedTask;
            }, CallbackFlags.OnChildChange | CallbackFlags.OnNotNull, triggerWithCurrent: false);

        root.Run = second;

        (string newKey, string oldKey) replacement = await replacementObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("stage-2", replacement.newKey);
        Assert.Equal("stage-1", replacement.oldKey);

        second.Stages["stage-3"] = new TestChildState { Name = "third" };

        string[] childKeys = await childChangeObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["stage-2", "stage-3"], childKeys);
        Assert.DoesNotContain(snapshots, snapshot => snapshot.Contains("stage-1,stage-3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NestedStateObject_WritesFromDifferentThreads_PreserveOrder_AndLastValueWins()
    {
        await StateTestHelpers.WithQueuedDispatcherAsync(async () =>
        {
            TestRootState root = new() { Child = new TestChildState() };
            TaskCompletionSource firstWriteQueued = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task writeFirst = Task.Run(() =>
            {
                root.Child.Name = "first";
                firstWriteQueued.SetResult();
            });

            Task writeSecond = Task.Run(async () =>
            {
                await firstWriteQueued.Task;
                root.Child.Name = "second";
            });

            await Task.WhenAll(writeFirst, writeSecond);

            string finalValue = await StateServiceDispatcher.DispatchAsync(() => root.Child.Name);
            Assert.Equal("second", finalValue);
        });
    }

    [Fact]
    public async Task StatePath_WritesFromDifferentThreads_ToDifferentNestedPaths_DoNotLoseAssignedValues()
    {
        await StateTestHelpers.WithQueuedDispatcherAsync(async () =>
        {
            TestRootState root = new() { Child = new TestChildState() };
            StateObjectPathBuilder<string> titlePath = StatePath.For(root).Property(TestRootState.TitleProperty);
            StateObjectPathBuilder<string> childNamePath = StatePath.For(root).Property(TestRootState.ChildProperty).Property(TestChildState.NameProperty);
            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task writeTitle = Task.Run(async () =>
            {
                await start.Task;
                Assert.True(titlePath.SetValue("updated-title"));
            });

            Task writeChild = Task.Run(async () =>
            {
                await start.Task;
                Assert.True(childNamePath.SetValue("updated-child"));
            });

            start.SetResult();
            await Task.WhenAll(writeTitle, writeChild);

            (string Title, string ChildName) snapshot = await StateServiceDispatcher.DispatchAsync(() => (root.Title, root.Child.Name));
            Assert.Equal("updated-title", snapshot.Title);
            Assert.Equal("updated-child", snapshot.ChildName);
        });
    }

    [Fact]
    public async Task StatePath_WritesFromDifferentThreads_ToSameNestedPath_PreserveOrder_AndLastValueWins()
    {
        await StateTestHelpers.WithQueuedDispatcherAsync(async () =>
        {
            TestRootState root = new() { Child = new TestChildState() };
            StateObjectPathBuilder<string> childNamePath = StatePath.For(root).Property(TestRootState.ChildProperty).Property(TestChildState.NameProperty);
            TaskCompletionSource firstWriteQueued = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task writeFirst = Task.Run(() =>
            {
                Assert.True(childNamePath.SetValue("first"));
                firstWriteQueued.SetResult();
            });

            Task writeSecond = Task.Run(async () =>
            {
                await firstWriteQueued.Task;
                Assert.True(childNamePath.SetValue("second"));
            });

            await Task.WhenAll(writeFirst, writeSecond);

            string finalValue = await StateServiceDispatcher.DispatchAsync(() => childNamePath.GetValue()!);
            Assert.Equal("second", finalValue);
        });
    }

    public sealed class TestRootState : StateObject
    {
        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public static StateProperty<string> TitleProperty { get; } = Property(nameof(Title), "title");

        public TestChildState Child { get => GetValue(ChildProperty); set => SetValue(ChildProperty, value); }
        public static StateProperty<TestChildState> ChildProperty { get; } = Property(nameof(Child), new TestChildState());

        public StateDictionary<TestChildState> Children { get => GetValue(ChildrenProperty); set => SetValue(ChildrenProperty, value); }
        public static StateProperty<StateDictionary<TestChildState>> ChildrenProperty { get; } = Property(nameof(Children), new StateDictionary<TestChildState>());
    }

    public sealed class TestChildState : StateObject
    {
        public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
        public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), "");
    }

    public sealed class TestRunHolderState : StateObject
    {
        public TestRunState Run { get => GetValue(RunProperty); set => SetValue(RunProperty, value); }
        public static StateProperty<TestRunState> RunProperty { get; } = Property(nameof(Run), new TestRunState());
    }

    public sealed class TestRunState : StateObject
    {
        public StateDictionary<TestChildState> Stages { get => GetValue(StagesProperty); set => SetValue(StagesProperty, value); }
        public static StateProperty<StateDictionary<TestChildState>> StagesProperty { get; } = Property(nameof(Stages), new StateDictionary<TestChildState>());
    }
}