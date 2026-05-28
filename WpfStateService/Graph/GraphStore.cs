using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using WpfStateService.Callbacks;
using WpfStateService.StateServiceObject;

namespace WpfStateService.Graph;

internal static class GraphStore
{
    private record GraphNode(WeakReference<StateObject> StateObjectRef)
    {
        internal int ReferenceCount { get; set; }
    }

    private record GuidWithProperty(Guid Guid, string PropertyToChild);

    private static readonly object _graphLock = new();
    private static readonly Dictionary<Guid, GraphNode> _nodes = [];
    private static readonly Dictionary<GuidWithProperty, Guid> _edges = [];
    private static readonly ConcurrentDictionary<StateObjectPath, CallbackCollection> _callbacks = [];

    internal static void AddNode(StateObject stateObject)
    {
        lock (_graphLock)
        {
            if (_nodes.ContainsKey(stateObject.Id)) return;
            _nodes.Add(stateObject.Id, new GraphNode(new WeakReference<StateObject>(stateObject)));
        }
    }

    internal static void RemoveNode(Guid guid)
    {
        lock (_graphLock)
        {
            if (!_nodes.ContainsKey(guid)) return;
            foreach (GuidWithProperty parent in FindParents(guid))
            {
                Unlink(parent.Guid, parent.PropertyToChild, guid);
            }
            _nodes.Remove(guid);
        }
    }

    internal static void Link(Guid parent, string propertyName, Guid child)
    {
        lock (_graphLock)
        {
            bool found = false;
            if ((found = _edges.TryGetValue(new GuidWithProperty(parent, propertyName), out Guid oldChild)) && oldChild == child) return;
            if (found) DegradeReference(oldChild);

            _edges[new GuidWithProperty(parent, propertyName)] = child;
        }
    }

    internal static void Unlink(Guid parent, string propertyName)
    {
        Unlink(parent, propertyName, null);
    }

    internal static void Unlink(Guid parent, string propertyName, Guid? expectedChild)
    {
        lock (_graphLock)
        {
            if (_edges.TryGetValue(new GuidWithProperty(parent, propertyName), out var child))
            {
                if (expectedChild is not null && child != expectedChild.Value)
                    return;

                _edges.Remove(new GuidWithProperty(parent, propertyName));
                DegradeReference(child);
            }
        }
    }

    internal static bool TryQueryNode(Guid guid, [NotNullWhen(true)] out StateObject? state)
    {
        lock (_graphLock)
        {
            state = null;
            return _nodes.TryGetValue(guid, out var node) && node.StateObjectRef.TryGetTarget(out state);
        }
    }

    internal static StateObject? QueryChildNode(Guid parent, string propertyName)
    {
        Guid child;
        lock (_graphLock)
        {
            if (!_edges.TryGetValue(new GuidWithProperty(parent, propertyName), out child)) return null;
        }

        return TryQueryNode(child, out var state) ? state : null;
    }

    internal static void SignalChange(Guid guid, string propertyName, object? value, object? oldValue)
    {
        static List<StateObjectPath> GetParentPaths(Guid guid)
        {
            List<GuidWithProperty> parents = FindParents(guid);
            if (parents.Count == 0) return [new StateObjectPath(guid)];
            List<StateObjectPath> parentPaths = [];
            foreach (GuidWithProperty parent in parents)
            {
                List<StateObjectPath> paths = GetParentPaths(parent.Guid);
                foreach (StateObjectPath path in paths)
                {
                    path.PropertySteps.Add(parent.PropertyToChild);
                }
                parentPaths.AddRange(paths);
            }
            return parentPaths;
        }

        List<StateObjectPath> parentPaths = GetParentPaths(guid);
        foreach (StateObjectPath parentPath in parentPaths)
        {
            StateObjectPath changedPath = parentPath.Clone();
            changedPath.PropertySteps.Add(propertyName);
            if (_callbacks.TryGetValue(changedPath, out var callback))
                callback.Invoke(value, oldValue, CallbackCallingFlags.None);

            foreach (var childCallback in _callbacks)
            {
                if (!TryGetRelativeSteps(childCallback.Key, changedPath, out string[] relativeSteps) || relativeSteps.Length == 0)
                    continue;

                object? childValue = EvaluateRelativeValue(value, relativeSteps);
                object? oldChildValue = EvaluateRelativeValue(oldValue, relativeSteps);
                childCallback.Value.Invoke(childValue, oldChildValue, CallbackCallingFlags.CalledForParent);
            }

            StateObjectPath ancestorPath = changedPath;
            while ((ancestorPath = ancestorPath.GetForParent()) is not null)
            {
                object? ancestorValue = ancestorPath.GetValue();
                if (_callbacks.TryGetValue(ancestorPath, out var parentCallback))
                    parentCallback.Invoke(ancestorValue, ancestorValue, CallbackCallingFlags.CalledForChild);
            }
        }
    }

    internal static void AddCallback<T>(StateObjectPath path, StateCallback<T> callback, CallbackFlags flags)
    {
        if (!_callbacks.ContainsKey(path)) _callbacks[path] = new CallbackCollection();
        _callbacks[path].AddCallback(callback, flags);
    }

    internal static void RunGC()
    {
        foreach (var callback in _callbacks)
        {
            callback.Value.RunGC();
        }
    }

    private static void DegradeReference(Guid guid)
    {
        _nodes[guid].ReferenceCount--;
    }

    private static bool TryGetRelativeSteps(StateObjectPath descendantPath, StateObjectPath ancestorPath, out string[] relativeSteps)
    {
        relativeSteps = [];

        if (descendantPath.RootId != ancestorPath.RootId)
            return false;

        if (descendantPath.PropertySteps.Count < ancestorPath.PropertySteps.Count)
            return false;

        for (int index = 0; index < ancestorPath.PropertySteps.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(descendantPath.PropertySteps[index], ancestorPath.PropertySteps[index]))
                return false;
        }

        relativeSteps = [.. descendantPath.PropertySteps.Skip(ancestorPath.PropertySteps.Count)];
        return true;
    }

    private static object? EvaluateRelativeValue(object? rootValue, IEnumerable<string> relativeSteps)
    {
        object? current = rootValue;

        foreach (string step in relativeSteps)
        {
            if (current is not StateObject stateObject)
                return null;

            try
            {
                current = stateObject.GetValue<object>(step);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        return current;
    }

    private static List<GuidWithProperty> FindParents(Guid guid)
    {
        return [.. _edges.Where(x => x.Value == guid).Select(x => x.Key)];
    }
}