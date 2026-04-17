using WpfStateService.StateServiceObject;

namespace WpfStateService.Graph;

public class StateObjectPath : IEquatable<StateObjectPath>
{
    internal Guid RootId { get; private set; }
    internal List<string> PropertySteps { get; } = [];

    internal StateObjectPath(Guid rootId)
    {
        RootId = rootId;
    }

    internal void SetParent(Guid parent, string propertyName)
    {
        RootId = parent;
        PropertySteps.Insert(0, propertyName);
    }

    internal StateObjectPath Clone()
    {
        StateObjectPath newPath = new StateObjectPath(RootId);
        newPath.PropertySteps.AddRange(PropertySteps);
        return newPath;
    }

    internal StateObjectPath? GetForParent()
    {
        if (PropertySteps.Count == 0) return null;
        StateObjectPath newPath = new StateObjectPath(RootId);
        newPath.PropertySteps.AddRange(PropertySteps.SkipLast(1));
        return newPath;
    }

    internal bool IsAChildOf(Guid parent)
    {
        if (!GraphStore.TryQueryNode(RootId, out StateObject? stateObject)) return false;
        if (PropertySteps.Count == 0) return false;
        if (stateObject.Id == parent) return true;
        foreach (var item in PropertySteps.SkipLast(1))
        {
            stateObject = GraphStore.QueryChildNode(stateObject!.Id, item);
            if (stateObject is null) return false;
            if (parent == stateObject.Id) return true;
        }
        return false;
    }

    public object? GetValue()
    {
        if (!GraphStore.TryQueryNode(RootId, out StateObject? stateObject)) return null;
        if (PropertySteps.Count == 0) return stateObject;
        foreach (var item in PropertySteps.SkipLast(1))
        {
            stateObject = GraphStore.QueryChildNode(stateObject!.Id, item);
            if (stateObject is null) return null;
        }
        return stateObject.GetValue<object>(PropertySteps.Last());
    }

    public bool SetValue<T>(T value)
    {
        if (!GraphStore.TryQueryNode(RootId, out StateObject? stateObject)) return false;
        if (PropertySteps.Count == 0) return false;
        foreach (var item in PropertySteps.SkipLast(1))
        {
            stateObject = GraphStore.QueryChildNode(stateObject!.Id, item);
            if (stateObject is null) return false;
        }
        stateObject.SetValue<T>(PropertySteps.Last(), value);
        return true;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as StateObjectPath);
    }

    public bool Equals(StateObjectPath? other)
    {
        if (other is null) return false;

        return RootId == other.RootId &&
               PropertySteps.SequenceEqual(other.PropertySteps);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = RootId.GetHashCode();
            foreach (var step in PropertySteps)
            {
                hash = hash * 31 + (step?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}