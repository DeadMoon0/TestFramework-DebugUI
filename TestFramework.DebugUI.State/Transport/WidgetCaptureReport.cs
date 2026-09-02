namespace TestFramework.DebugUI.State.Transport;

/// <summary>
/// What came of asking a run for a fresh look at itself.
/// </summary>
/// <remarks>
/// Four outcomes worth telling apart, because a reader who pressed a button and saw nothing appear
/// deserves to know which of them happened: the run is gone, it is already being asked, it never
/// answered, or it answered and had nothing to show. Only the last of those is about the run's
/// contents rather than about the asking.
/// </remarks>
public sealed record WidgetCaptureReport
{
    /// <summary>The run is not attached, so there was nobody to ask.</summary>
    public static WidgetCaptureReport NotAttached { get; } = new()
    {
        Detail = "The run is no longer attached."
    };

    /// <summary>An earlier request is still in flight.</summary>
    public static WidgetCaptureReport AlreadyAsking { get; } = new()
    {
        Detail = "The run is already being asked for a fresh look."
    };

    /// <summary>
    /// The request went out and nothing came back in time.
    /// </summary>
    /// <remarks>
    /// Not a failure, which is why it says so: the run is unharmed and anything it does capture will
    /// still arrive on its own, as an ordinary widget.
    /// </remarks>
    public static WidgetCaptureReport Unanswered { get; } = new()
    {
        Detail = "The run has not answered yet. Anything it captures will still arrive."
    };

    /// <summary>Gets a value indicating whether the run answered at all.</summary>
    public bool Answered { get; init; }

    /// <summary>Gets how many widgets the run recorded while serving the request.</summary>
    public int Captured { get; init; }

    /// <summary>Gets what the run said about it, or why it could not be asked.</summary>
    public string? Detail { get; init; }
}
