namespace WpfStateService.StateServiceObject;

public class StateProperty<T>
{
    public required string Name { get; init; }
    public required T DefaultValue { get; init; }

    internal StateProperty() { }
}