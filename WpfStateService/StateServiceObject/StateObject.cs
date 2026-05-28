using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using WpfStateService.Graph;

namespace WpfStateService.StateServiceObject;

public abstract class StateObject
{
    private static readonly object _defaultsSync = new();
    private static readonly Dictionary<Type, Dictionary<string, object?>> _defaults = [];
    private static readonly HashSet<Type> _defaultsInitializing = [];

    protected static StateProperty<T> Property<T>(string propertyName, T defaultValue)
    {
        Type implem = GetCallingType();
        lock (_defaultsSync)
        {
            if (!_defaults.ContainsKey(implem))
                _defaults[implem] = new Dictionary<string, object?>();

            _defaults[implem][propertyName] = defaultValue;
        }

        return new StateProperty<T>() { Name = propertyName, DefaultValue = defaultValue };
    }

    private readonly ConcurrentDictionary<string, object?> _objectStore = [];

    public Guid Id { get; }

    protected StateObject()
    {
        Type runtimeType = GetType();
        EnsureDefaultsInitialized(runtimeType);

        TaskCompletionSource tcs = new TaskCompletionSource();

        Id = Guid.NewGuid();
        StateServiceDispatcher.Dispatch(() =>
        {
            Dictionary<string, object?>? defaults;
            lock (_defaultsSync)
            {
                _defaults.TryGetValue(runtimeType, out defaults);
            }

            if (defaults is not null)
            foreach (var item in defaults)
            {
                object? defaultValue = item.Value;
                if (defaultValue is StateObject stateObject)
                {
                    EnsureDefaultsInitialized(stateObject.GetType());
                    defaultValue = Activator.CreateInstance(stateObject.GetType()) ?? throw new InvalidOperationException($"Could not create default state object instance for {stateObject.GetType().FullName}.");
                }

                _objectStore[item.Key] = defaultValue;
                if (defaultValue is StateObject stateObjectInstance) GraphStore.Link(Id, item.Key, stateObjectInstance.Id);
            }
            GraphStore.AddNode(this);
            tcs.SetResult();
        });
        tcs.Task.Wait();
    }

    ~StateObject()
    {
        StateServiceDispatcher.Dispatch(() =>
        {
            GraphStore.RemoveNode(Id);
        });
    }

    public T GetValue<T>(StateProperty<T> property)
    {
        return GetValue<T>(property.Name);
    }

    public T GetValue<T>(string propertyName)
    {
        return (T)_objectStore[propertyName]!;
    }

    public void SetValue<T>(StateProperty<T> property, T value)
    {
        SetValue(property.Name, value);
    }

    public void SetValue<T>(string propertyName, T value)
    {
        StateServiceDispatcher.Dispatch(() =>
        {
            object? oldValue = _objectStore[propertyName];
            if (oldValue is StateObject oldStateObject)
                GraphStore.Unlink(Id, propertyName, oldStateObject.Id);

            _objectStore[propertyName] = value;
            if (value is StateObject stateObject) GraphStore.Link(Id, propertyName, stateObject.Id);
            GraphStore.SignalChange(Id, propertyName, value, oldValue);
        });
    }

    protected StateProperty<T> PropertyDynamic<T>(string propertyName, T defaultValue)
    {
        _objectStore[propertyName] = defaultValue;
        return new StateProperty<T>() { Name = propertyName, DefaultValue = defaultValue };
    }

    protected void RemovePropertyDynamic(string propertyName)
    {
        _objectStore.TryRemove(propertyName, out _);
    }

    protected void RemoveValue(string propertyName)
    {
        StateServiceDispatcher.Dispatch(() =>
        {
            if (!_objectStore.TryRemove(propertyName, out object? oldValue))
                return;

            if (oldValue is StateObject oldStateObject)
                GraphStore.Unlink(Id, propertyName, oldStateObject.Id);

            GraphStore.SignalChange(Id, propertyName, null, oldValue);
        });
    }

    protected List<string> GetPropertyNames() => [.. _objectStore.Keys];

    private static void EnsureDefaultsInitialized(Type runtimeType)
    {
        bool shouldInitialize;
        lock (_defaultsSync)
        {
            shouldInitialize = !_defaults.ContainsKey(runtimeType) && !_defaultsInitializing.Contains(runtimeType);
            if (shouldInitialize)
                _defaultsInitializing.Add(runtimeType);
        }

        if (shouldInitialize)
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(runtimeType.TypeHandle);
            }
            finally
            {
                lock (_defaultsSync)
                {
                    _defaultsInitializing.Remove(runtimeType);
                }
            }
        }

        List<Type> nestedStateTypes = [];
        lock (_defaultsSync)
        {
            if (_defaults.TryGetValue(runtimeType, out Dictionary<string, object?>? defaults))
            {
                foreach (object? defaultValue in defaults.Values)
                {
                    if (defaultValue is StateObject stateObject)
                        nestedStateTypes.Add(stateObject.GetType());
                }
            }
        }

        foreach (Type nestedStateType in nestedStateTypes)
            EnsureDefaultsInitialized(nestedStateType);
    }

    private static Type GetCallingType()
    {
        var stackTrace = new StackTrace();

        for (int i = 1; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i)?.GetMethod();
            var type = method?.ReflectedType;

            if (type != null && type != typeof(StateObject) && type.IsSubclassOf(typeof(StateObject)))
            {
                return type;
            }
        }

        return typeof(StateObject);
    }

}