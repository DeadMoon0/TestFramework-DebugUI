using System.Threading.Tasks;
using TestFramework.DebugUI.Tests.Support;
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
}