namespace WpfStateService.Callbacks;

public delegate Task StateCallback<T>(T value, T oldValue);
internal delegate Task StateCallbackGeneric(object? value, object? oldValue);