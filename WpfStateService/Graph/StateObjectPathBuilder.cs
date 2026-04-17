using WpfStateService.Callbacks;
using WpfStateService.Dispatching;
using WpfStateService.StateServiceObject;

namespace WpfStateService.Graph;

public static class StatePath
{
    public static StateObjectPathBuilder<T> For<T>(T stateRoot) where T : StateObject
    {
        return new StateObjectPathBuilder<T>(stateRoot.Id, []);
    }
}

public class StateObjectPathBuilder<T>
{
    private readonly Guid _root;
    private readonly string[] _propertySteps;

    internal StateObjectPathBuilder(Guid root, string[] propertySteps)
    {
        _root = root;
        _propertySteps = propertySteps;
    }

    public StateObjectPathBuilder<TNext> Property<TNext>(StateProperty<TNext> property)
    {
        return new StateObjectPathBuilder<TNext>(_root, [.. _propertySteps, property.Name]);
    }

    public StateObjectPathBuilder<TNext> PropertyKey<TNext>(string key)
    {
        return new StateObjectPathBuilder<TNext>(_root, [.. _propertySteps, key]);
    }

    public void CallbackAsync(StateCallback<T> callback, CallbackFlags flags, bool triggerWithCurrent = true)
    {
        GraphStore.AddCallback(this.ToPath(), callback, flags);
        if (triggerWithCurrent)
        {
            T? value = GetValue();
            if (value is not null) StateCommonDispatcher.StateDispatcher.DispatchCallbackAsync(() => callback(value, default));
        }
    }

    public T? GetValue()
    {
        var val = this.ToPath().GetValue();
        if (val is null) return default;
        return (T)val;
    }

    public bool SetValue(T value)
    {
        return this.ToPath().SetValue(value);
    }

    private StateObjectPath ToPath()
    {
        var path = new StateObjectPath(_root);
        path.PropertySteps.AddRange(_propertySteps);
        return path;
    }
}