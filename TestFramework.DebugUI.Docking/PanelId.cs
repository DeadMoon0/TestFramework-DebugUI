namespace TestFramework.DebugUI.Docking;

/// <summary>
/// Every panel that can be arranged.
/// </summary>
/// <remarks>
/// <para>
/// A named enum rather than a type name, for the same reason the pipe protocol names its own: an arrangement
/// is written to a settings file and read back by a later build, and a panel's class being renamed must not
/// silently move it somewhere else or drop it. The name written here is the contract.
/// </para>
/// <para>
/// Only the surfaces that are a view onto the selected run, plus the runs page. The find bar, the annotation
/// palette, settings, the notification feed and the export sheet are deliberately absent: a find bar belongs
/// over what it searches, the palette is welded to the board's own transform, and the other three are a page,
/// a dropdown and a dialog. Making them arrangeable would mean arranging things nobody arranges.
/// </para>
/// </remarks>
public enum PanelId
{
    /// <summary>The project, class, test and execution tree.</summary>
    Runs,

    /// <summary>The variables and artifacts the run reported.</summary>
    Values,

    /// <summary>Everything about the selected step.</summary>
    Step,

    /// <summary>Whether the run as a whole can be trusted.</summary>
    Summary,

    /// <summary>One value in full.</summary>
    Inspector,

    /// <summary>The whole journal, as a page.</summary>
    Home
}
