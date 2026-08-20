using System;
using System.Collections.Immutable;
using System.Linq;
using System.Windows.Controls;
using TestFramework.DebugUI.Controls.Detail;
using TestFramework.DebugUI.Controls.Home;
using TestFramework.DebugUI.Controls.Runs;
using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.Controls.Dock;

/// <summary>
/// Everything the host needs to know about one panel that the arrangement itself must not.
/// </summary>
/// <remarks>
/// The split is deliberate. The arrangement is where panels <em>are</em>, and it is pure data that can be
/// written to a file and tested without a window. This is what a panel <em>is</em> — a control, a name, a
/// picture — all of which are WPF, and none of which belongs in something a settings file holds.
/// </remarks>
internal sealed record PanelDescriptor
{
    /// <summary>Gets the panel this describes.</summary>
    public required PanelId Id { get; init; }

    /// <summary>Gets the name on its header, its tab and its tooltip.</summary>
    /// <remarks>
    /// One name in one place. The panels each carry a heading of their own today and the host draws another
    /// above it; the headings inside them go, because a panel titled twice looks like two panels.
    /// </remarks>
    public required string Title { get; init; }

    /// <summary>Gets the theme key of the icon that stands for it in the title bar.</summary>
    public required string IconKey { get; init; }

    /// <summary>
    /// Gets where the panel opens when nobody has said otherwise.
    /// </summary>
    /// <remarks>
    /// Held here rather than in the arrangement because it is a fact about the panel, not about any particular
    /// arrangement of them — which is why <see cref="DockLayout.Reveal"/> takes it as an argument instead of
    /// guessing.
    /// </remarks>
    public required DockSide DefaultSide { get; init; }

    /// <summary>Gets how to build it.</summary>
    public required Func<UserControl> Create { get; init; }
}

/// <summary>
/// The panels the tool can arrange, defined once.
/// </summary>
/// <remarks>
/// One table, for the same reason <see cref="Shortcuts"/> is one table: the title bar's strip, the host's
/// headers and the default arrangement all read from it, and three copies of "what panels are there" would
/// start agreeing and end disagreeing.
/// </remarks>
internal static class PanelRegistry
{
    /// <summary>Every panel, in the order the title bar offers them.</summary>
    /// <remarks>
    /// Ordered by how often they are reached for rather than alphabetically: which run, then what it produced,
    /// then the step, then the questions asked about the whole thing.
    /// </remarks>
    public static ImmutableList<PanelDescriptor> All { get; } =
    [
        new PanelDescriptor
        {
            Id = PanelId.Runs,
            Title = "RUNS",
            IconKey = "IconPanelRuns",
            DefaultSide = DockSide.Left,
            Create = () => new UC_Runs()
        },
        new PanelDescriptor
        {
            Id = PanelId.Values,
            Title = "VALUES",
            IconKey = "IconPanelValues",

            // Under the tree in the same rail, which is where it has always been: what a run produced is read
            // beside which run you are reading.
            DefaultSide = DockSide.Left,
            Create = () => new UC_ValueRail()
        },
        new PanelDescriptor
        {
            Id = PanelId.Step,
            Title = "STEP",
            IconKey = "IconPanelStep",
            DefaultSide = DockSide.Right,
            Create = () => new UC_StepDetail()
        },
        new PanelDescriptor
        {
            Id = PanelId.Summary,
            Title = "SUMMARY",
            IconKey = "IconSummary",
            DefaultSide = DockSide.Right,
            Create = () => new UC_RunSummary()
        },
        new PanelDescriptor
        {
            Id = PanelId.Inspector,
            Title = "VALUE",
            IconKey = "IconPanelInspector",
            DefaultSide = DockSide.Right,
            Create = () => new UC_ValueInspector()
        },
        new PanelDescriptor
        {
            // The one placement that is not a preference: the runs page is a table six columns wide, and in a
            // three-hundred-pixel rail every one of them would be an ellipsis.
            Id = PanelId.Home,
            Title = "RUNS PAGE",
            IconKey = "IconHome",
            DefaultSide = DockSide.Center,
            Create = () => new UC_Home()
        }
    ];

    /// <summary>The description of one panel.</summary>
    public static PanelDescriptor Of(PanelId panel)
        => All.Find(descriptor => descriptor.Id == panel)
           ?? throw new InvalidOperationException($"No panel is registered for {panel}.");

    /// <summary>Where a panel opens when it has no place of its own.</summary>
    public static DockSide DefaultSideOf(PanelId panel) => Of(panel).DefaultSide;
}
