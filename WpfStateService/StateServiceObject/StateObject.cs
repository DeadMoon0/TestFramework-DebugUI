using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using WpfStateService.Graph;

namespace WpfStateService.StateServiceObject;

public abstract class StateObject
{
    private static readonly Dictionary<Type, Dictionary<string, object?>> _defaults = [];

    protected static StateProperty<T> Property<T>(string propertyName, T defaultValue)
    {
        Type implem = GetCallingType();
        if (!_defaults.ContainsKey(implem)) _defaults[implem] = new Dictionary<string, object?>();
        _defaults[implem][propertyName] = defaultValue;
        return new StateProperty<T>() { Name = propertyName, DefaultValue = defaultValue };
    }

    private readonly ConcurrentDictionary<string, object?> _objectStore = [];

    public Guid Id { get; }

    protected StateObject()
    {
        //TODO: Maybe not call this every StateObject Creation :)
        foreach (Type type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes()).Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(StateObject))))
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        }

        TaskCompletionSource tcs = new TaskCompletionSource();

        Id = Guid.NewGuid();
        StateServiceDispatcher.Dispatch(() =>
        {
            if (_defaults.ContainsKey(this.GetType())) foreach (var item in _defaults[this.GetType()])
            {
                _objectStore[item.Key] = item.Value;
                if (item.Value is StateObject stateObject) GraphStore.Link(Id, item.Key, stateObject.Id);
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

    protected List<string> GetPropertyNames() => [.. _objectStore.Keys];

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