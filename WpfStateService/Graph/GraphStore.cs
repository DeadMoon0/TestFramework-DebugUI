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

    private static readonly Dictionary<Guid, GraphNode> _nodes = [];
    private static readonly Dictionary<GuidWithProperty, Guid> _edges = [];
    private static readonly ConcurrentDictionary<StateObjectPath, CallbackCollection> _callbacks = [];

    internal static void AddNode(StateObject stateObject)
    {
        if (_nodes.ContainsKey(stateObject.Id)) return;
        _nodes.Add(stateObject.Id, new GraphNode(new WeakReference<StateObject>(stateObject)));
    }

    internal static void RemoveNode(Guid guid)
    {
        if (_nodes.ContainsKey(guid)) return;
        foreach (GuidWithProperty parent in FindParents(guid))
        {
            Unlink(parent.Guid, parent.PropertyToChild);
        }
        _nodes.Remove(guid);
    }

    internal static void Link(Guid parent, string propertyName, Guid child)
    {
        bool found = false;
        if ((found = _edges.TryGetValue(new GuidWithProperty(parent, propertyName), out Guid oldChild)) && oldChild == child) return;
        if (found) DegradeReference(oldChild);

        _edges[new GuidWithProperty(parent, propertyName)] = child;
    }

    internal static void Unlink(Guid parent, string propertyName)
    {
        if (_edges.TryGetValue(new GuidWithProperty(parent, propertyName), out var child))
        {
            _edges.Remove(new GuidWithProperty(parent, propertyName));
            DegradeReference(child);
        }
    }

    internal static bool TryQueryNode(Guid guid, [NotNullWhen(true)] out StateObject? state)
    {
        state = null;
        return _nodes.TryGetValue(guid, out var node) && node.StateObjectRef.TryGetTarget(out state);
    }

    internal static StateObject? QueryChildNode(Guid parent, string propertyName)
    {
        if (!_edges.TryGetValue(new GuidWithProperty(parent, propertyName), out Guid child)) return null;
        return TryQueryNode(child, out var state) ? state : null;
    }

    internal static void SignalChange(Guid guid, string propertyName, object? value, object? oldValue)
    {
        //TODO: Fix. It is so Bad and Slow and Stupid ...
        static List<StateObjectPath> GetParentsPaths(Guid guid)
        {
            List<GuidWithProperty> parents = FindParents(guid);
            if (parents.Count == 0) return [new StateObjectPath(guid)];
            List<StateObjectPath> parentPaths = [];
            foreach (GuidWithProperty parent in parents)
            {
                List<StateObjectPath> paths = GetParentsPaths(parent.Guid);
                foreach (StateObjectPath path in paths)
                {
                    path.PropertySteps.Add(parent.PropertyToChild);
                }
                parentPaths.AddRange(paths);
            }
            return parentPaths;
        }

        List<StateObjectPath> paths = GetParentsPaths(guid);
        foreach (StateObjectPath path in paths)
        {
            path.PropertySteps.Add(propertyName);
            if(_callbacks.TryGetValue(path, out var callback)) callback.Invoke(value, oldValue, CallbackCallingFlags.None);

            foreach (var childCallback in _callbacks.Where(x => x.Key.IsAChildOf(guid)))
            {
                object? val = childCallback.Key.GetValue();
                childCallback.Value.Invoke(val, val, CallbackCallingFlags.CalledForParent);
            }

            StateObjectPath parentPath = path;
            while ((parentPath = parentPath.GetForParent()) is not null)
            {
                object? val = parentPath.GetValue();
                if (_callbacks.TryGetValue(parentPath, out var parentCallback)) parentCallback.Invoke(val, val, CallbackCallingFlags.CalledForChild);
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

    private static List<GuidWithProperty> FindParents(Guid guid)
    {
        return [.. _edges.Where(x => x.Value == guid).Select(x => x.Key)];
    }
}