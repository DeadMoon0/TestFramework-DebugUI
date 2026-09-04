namespace TestFramework.DebugUI.Theme;

/// <summary>
/// The names the code calls the theme's resources by.
/// </summary>
/// <remarks>
/// <para>
/// A resource is fetched by string, and a string that is wrong is wrong at the moment the control is
/// drawn — as a missing brush on one card in one panel, in a build that compiled and a suite that
/// passed. Naming them here turns a typo into a compiler error, and a key deleted from the theme into
/// a failing test, because <c>ThemeKeysTests</c> asks the theme for every name declared below.
/// </para>
/// <para>
/// Each constant is spelled exactly as its key, and the test insists on it. That is the whole contract:
/// renaming a key in the theme forces the same rename here, and there is no second place where the two
/// spellings could quietly disagree.
/// </para>
/// <para>
/// Only what the code names is here. Keys used solely from XAML are left out — a <c>StaticResource</c>
/// that cannot be found fails when the window is built, loudly and immediately, which is the protection
/// this class exists to give the ones fetched by hand.
/// </para>
/// </remarks>
internal static class ThemeKeys
{
    // Surfaces: what things sit on, from the window's own back to a card lifted off it.
    public const string SurfaceSunken = "SurfaceSunken";
    public const string SurfaceRaised = "SurfaceRaised";
    public const string SurfaceRaisedHover = "SurfaceRaisedHover";
    public const string SurfaceCard = "SurfaceCard";
    public const string SurfaceOverlay = "SurfaceOverlay";
    public const string PanelEdge = "PanelEdge";

    /// <summary>The outline of a grouped icon — a brush, despite reading like one of the icons below.</summary>
    public const string IconGroupEdge = "IconGroupEdge";

    // Text, in the three weights everything is written in.
    public const string TextPrimary = "TextPrimary";
    public const string TextSecondary = "TextSecondary";
    public const string TextFaint = "TextFaint";

    // Lifecycle: the colours a step, a run and a log line all report their state in.
    public const string StateNotRun = "StateNotRun";
    public const string StateRunning = "StateRunning";
    public const string StateComplete = "StateComplete";
    public const string StateError = "StateError";
    public const string StateTimeout = "StateTimeout";
    public const string StateSkipped = "StateSkipped";
    public const string StatePaused = "StatePaused";

    /// <summary>The one colour that means "this is what you are looking at".</summary>
    public const string Accent = "Accent";

    // A comparison, line by line.
    public const string DiffAddedSurface = "DiffAddedSurface";
    public const string DiffAddedText = "DiffAddedText";
    public const string DiffRemovedSurface = "DiffRemovedSurface";
    public const string DiffRemovedText = "DiffRemovedText";
    public const string DiffContextText = "DiffContextText";

    // What a reader draws in, and what their marks are lifted off the board by.
    public const string InkWhite = "InkWhite";
    public const string InkCyan = "InkCyan";
    public const string InkMagenta = "InkMagenta";
    public const string InkViolet = "InkViolet";
    public const string InkOrange = "InkOrange";
    public const string InkHalo = "InkHalo";

    // What flows between steps.
    public const string FlowVariable = "FlowVariable";
    public const string FlowArtifact = "FlowArtifact";
    public const string PipeShadow = "PipeShadow";

    // Text styles, as opposed to the brushes above.
    public const string PanelHeading = "PanelHeading";
    public const string BodyText = "BodyText";
    public const string MutedText = "MutedText";
    public const string CodeText = "CodeText";

    // Icons, as geometry rather than as pictures, so they take the colour of whatever draws them.
    public const string IconClose = "IconClose";
    public const string IconChevron = "IconChevron";
    public const string IconPanelRuns = "IconPanelRuns";
    public const string IconPanelValues = "IconPanelValues";
    public const string IconPanelStep = "IconPanelStep";
    public const string IconPanelInspector = "IconPanelInspector";
    public const string IconHome = "IconHome";
    public const string IconSummary = "IconSummary";
    public const string IconCopy = "IconCopy";
    public const string IconTick = "IconTick";

    // Button styles.
    public const string IconButton = "IconButton";
    public const string CaptionIconButton = "CaptionIconButton";
}
