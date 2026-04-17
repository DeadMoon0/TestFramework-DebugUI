using System.Diagnostics;
using System.Reflection;

namespace WpfStateService.Callbacks;

internal class CallbackCollection
{
    private record WeakAction(MethodInfo Method, WeakReference<object> Target);
    private record CallbackItem(WeakAction WeakAction, CallbackFlags Flags);

    private readonly List<CallbackItem> _callbacks = [];

    internal void Invoke(object? value, object? oldValue, CallbackCallingFlags flags)
    {
        if (value is null) flags |= CallbackCallingFlags.IsNowNull;
        if (value?.Equals(oldValue) ?? oldValue is null) flags |= CallbackCallingFlags.IsEqual;

        StateServiceDispatcher.DispatchCallback(new CallbackChangeMessage
        (
            [.. _callbacks.Where(x => MeetsCallingFlagRequirements(flags, x.Flags)).Select(x => GetActionFromWeakAction(x.WeakAction)).Where(x => x is not null).Cast<StateCallbackGeneric>()],
            value,
            oldValue
        ));
    }

    internal void RunGC()
    {
        _callbacks.RemoveAll(x => !x.WeakAction.Target.TryGetTarget(out _));
    }

    internal void AddCallback<T>(StateCallback<T> callback, CallbackFlags flags)
    {
        Debug.Assert(callback.Target is not null, "Target of the Action cannot be NULL. This can happen if the Action is Static.");
        _callbacks.Add(new CallbackItem(new WeakAction(callback.Method, new WeakReference<object>(callback.Target)), flags));
    }

    private static bool MeetsCallingFlagRequirements(CallbackCallingFlags callingFlags, CallbackFlags callbackFlags)
    {
        if ((callbackFlags & CallbackFlags.OnChildChange) != CallbackFlags.OnChildChange && ((callingFlags & CallbackCallingFlags.CalledForChild) == CallbackCallingFlags.CalledForChild)) return false; // Is for Child
        if ((callbackFlags & CallbackFlags.OnValueDiffers) == CallbackFlags.OnValueDiffers && ((callingFlags & CallbackCallingFlags.IsEqual) == CallbackCallingFlags.IsEqual)) return false; // Is for Equal Filter
        if ((callbackFlags & CallbackFlags.OnNotNull) == CallbackFlags.OnNotNull && ((callingFlags & CallbackCallingFlags.IsNowNull) == CallbackCallingFlags.IsNowNull)) return false; // Is for Equal Filter
        return true;
    }

    private static StateCallbackGeneric? GetActionFromWeakAction(WeakAction weakAction)
    {
        if (!weakAction.Target.TryGetTarget(out object? target)) return null;
        return (value, oldValue) =>
        {
            return (Task)weakAction.Method.Invoke(target, [value, oldValue])!;
        };
    }
}