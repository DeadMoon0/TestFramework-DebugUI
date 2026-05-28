using System.Collections.Concurrent;
using WpfStateService;
using WpfStateService.Callbacks;
using WpfStateService.Common;
using WpfStateService.Dispatching;
using WpfStateService.Graph;
using WpfStateService.StateServiceObject;
using TestFramework.DebugUI.State;

namespace TestFramework.DebugUI.Tests;

public sealed class StateServiceIntegrityTests
{
    public StateServiceIntegrityTests()
    {
        Support.StateTestHelpers.EnsureDispatcherInitialized();
    }

    [Fact]
    public async Task StateDictionary_ConcurrentWrites_RetainAllEntries()
    {
        StateDictionary<string> dictionary = new StateDictionary<string>();

        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() => dictionary[$"key-{index}"] = $"value-{index}")));

        Support.StateTestHelpers.Eventually(() => dictionary.Count == 100, "Expected all concurrent state writes to be retained.");
        Assert.Equal(100, dictionary.Keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Enumerable.Range(0, 100), index => Assert.Equal($"value-{index}", dictionary[$"key-{index}"]));
    }

    [Fact]
    public async Task RunState_CanBeConstructedOnStateWorkerThread_WithoutDeadlocking()
    {
        TaskCompletionSource<RunState> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        StateServiceDispatcher.Dispatch(() =>
        {
            try
            {
                tcs.SetResult(new RunState());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        RunState runState = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(runState.Assertions);
        Assert.NotNull(runState.Artifacts);
        Assert.NotNull(runState.Variables);
        Assert.NotNull(runState.Stages);
    }

    [Fact]
    public async Task SequentialWrites_ToDifferentProperties_DoNotLoseAssignedValues()
    {
        await WithQueuedDispatcherAsync(async () =>
        {
            TransactionProofState state = new();

            for (int index = 1; index <= 200; index++)
            {
                state.Left = index;
                state.Right = index * 10;

                TransactionProofSnapshot snapshot = await StateServiceDispatcher.DispatchAsync(() =>
                    new TransactionProofSnapshot(state.Left, state.Right));

                Assert.Equal(index, snapshot.Left);
                Assert.Equal(index * 10, snapshot.Right);
            }
        });
    }

    [Fact]
    public async Task SequentialWrites_ToSameProperty_PreserveOrder_AndLastValueWins()
    {
        await WithQueuedDispatcherAsync(async () =>
        {
            OrderedValueState state = new();

            for (int index = 1; index <= 200; index++)
            {
                state.Value = index;
                state.Value = index * 10;

                int finalValue = await StateServiceDispatcher.DispatchAsync(() => state.Value);
                Assert.Equal(index * 10, finalValue);
            }
        });
    }

    [Fact]
    public async Task WritesFromDifferentThreads_ToDifferentProperties_DoNotLoseAssignedValues()
    {
        await WithQueuedDispatcherAsync(async () =>
        {
            for (int index = 1; index <= 200; index++)
            {
                TransactionProofState state = new();
                TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);

                Task writeLeft = Task.Run(async () =>
                {
                    await start.Task;
                    state.Left = index;
                });

                Task writeRight = Task.Run(async () =>
                {
                    await start.Task;
                    state.Right = index * 10;
                });

                start.SetResult();
                await Task.WhenAll(writeLeft, writeRight);

                TransactionProofSnapshot snapshot = await StateServiceDispatcher.DispatchAsync(() =>
                    new TransactionProofSnapshot(state.Left, state.Right));

                Assert.Equal(index, snapshot.Left);
                Assert.Equal(index * 10, snapshot.Right);
            }
        });
    }

    [Fact]
    public async Task OrderedWritesFromDifferentThreads_ToSameProperty_LastValueWins()
    {
        await WithQueuedDispatcherAsync(async () =>
        {
            for (int index = 1; index <= 200; index++)
            {
                OrderedValueState state = new();
                TaskCompletionSource firstWriteQueued = new(TaskCreationOptions.RunContinuationsAsynchronously);

                Task writeFirst = Task.Run(() =>
                {
                    state.Value = index;
                    firstWriteQueued.SetResult();
                });

                Task writeSecond = Task.Run(async () =>
                {
                    await firstWriteQueued.Task;
                    state.Value = index * 10;
                });

                await Task.WhenAll(writeFirst, writeSecond);

                int finalValue = await StateServiceDispatcher.DispatchAsync(() => state.Value);
                Assert.Equal(index * 10, finalValue);
            }
        });
    }

    [Fact]
    public async Task NestedDefaultStateObjects_AreIsolated_PerRootInstance()
    {
        await WithQueuedDispatcherAsync(async () =>
        {
            StableRootState first = new();
            StableRootState second = new();

            Assert.NotSame(first.Child, second.Child);
            Assert.NotSame(first.Child.Leaf, second.Child.Leaf);

            first.Child.Leaf.Name = "first-live";
            second.Child.Leaf.Name = "second-live";

            await StateServiceDispatcher.DispatchAsync(() => { });

            string currentFirst = await StateServiceDispatcher.DispatchAsync(() => first.Child.Leaf.Name);
            string currentSecond = await StateServiceDispatcher.DispatchAsync(() => second.Child.Leaf.Name);
            Assert.Equal("first-live", currentFirst);
            Assert.Equal("second-live", currentSecond);
        });
    }

    private static async Task WithQueuedDispatcherAsync(Func<Task> action)
    {
        IStateDispatcher previousDispatcher = StateCommonDispatcher.StateDispatcher;
        StateCommonDispatcher.StateDispatcher = null!;

        try
        {
            await action();
        }
        finally
        {
            StateCommonDispatcher.StateDispatcher = previousDispatcher;
        }
    }

    private sealed record TransactionProofSnapshot(int Left, int Right);

    private sealed class TransactionProofState : StateObject
    {
        public int Left { get => GetValue(LeftProperty); set => SetValue(LeftProperty, value); }
        public static StateProperty<int> LeftProperty { get; } = Property(nameof(Left), 0);

        public int Right { get => GetValue(RightProperty); set => SetValue(RightProperty, value); }
        public static StateProperty<int> RightProperty { get; } = Property(nameof(Right), 0);
    }

    private sealed class OrderedValueState : StateObject
    {
        public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public static StateProperty<int> ValueProperty { get; } = Property(nameof(Value), 0);
    }

    private sealed class StableRootState : StateObject
    {
        public StableChildState Child { get => GetValue(ChildProperty); set => SetValue(ChildProperty, value); }
        public static StateProperty<StableChildState> ChildProperty { get; } = Property(nameof(Child), new StableChildState());
    }

    private sealed class StableChildState : StateObject
    {
        public StableLeafState Leaf { get => GetValue(LeafProperty); set => SetValue(LeafProperty, value); }
        public static StateProperty<StableLeafState> LeafProperty { get; } = Property(nameof(Leaf), new StableLeafState());
    }

    private sealed class StableLeafState : StateObject
    {
        public string Name { get => GetValue(NameProperty); set => SetValue(NameProperty, value); }
        public static StateProperty<string> NameProperty { get; } = Property(nameof(Name), string.Empty);
    }
}