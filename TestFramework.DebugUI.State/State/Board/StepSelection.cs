namespace TestFramework.DebugUI.State.Board;

/// <summary>
/// Which step the detail panel is showing.
/// </summary>
/// <remarks>
/// A stage name and an index rather than the step itself: the graph is replaced wholesale as events
/// arrive, so holding the node would pin a stale copy and the panel would stop updating exactly when
/// the step started doing something.
/// </remarks>
public sealed record StepSelection
{
    /// <summary>Gets the stage containing the step.</summary>
    public required string StageName { get; init; }

    /// <summary>Gets the step's index within its stage.</summary>
    public required int StepId { get; init; }
}
