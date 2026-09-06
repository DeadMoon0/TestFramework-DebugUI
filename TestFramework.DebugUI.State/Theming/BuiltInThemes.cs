using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>
/// The themes that ship with the tool.
/// </summary>
/// <remarks>
/// <para>
/// Five identities in two modes each. An identity fixes a surface temperature, an accent and a
/// backdrop; a mode flips the surface ramp and re-tunes the lifecycle colours so they still read
/// against it. None of the light palettes is an inversion of its dark one — inverting a dark theme
/// gives you a grey theme, because the alpha washes that layer a dark window are black, and black over
/// a light ground is mud.
/// </para>
/// <para>
/// The two Glass themes paint nothing at all and lower <c>WindowTint</c> instead, so what shows behind
/// the window is the acrylic blur Windows already draws and whatever is on the desktop. They are why
/// several colours here are stronger than they look: an edge or a secondary label under Glass has to
/// hold against a ground that belongs to somebody else.
/// </para>
/// <para>
/// Data, and nothing else. There is no fallback here and no repair: every palette below is complete
/// because <see cref="ThemePalette"/> will not compile otherwise, so nothing at start-up has to check
/// that it is.
/// </para>
/// </remarks>
public static class BuiltInThemes
{
    /// <summary>The theme a fresh install opens in.</summary>
    /// <remarks>
    /// Slate Dark: the same graphite surfaces and the same azure accent the tool had before it had
    /// themes, so nothing a reader has learned to recognise has moved. Its backdrop is not the old one —
    /// blurred ridges lost to a hex field, because geometry survives the blur where waves turn to
    /// smoke — and anyone who wants the ridges back can have them, from the picker or from one line in
    /// a theme file.
    /// </remarks>
    public const string DefaultId = "slate-dark";

    /// <summary>Slate Dark. A honeycomb with cells missing, lit cool from the upper left. Quietest of the five &#8212; it is the default, so its ridges carry barely any colour at all: graphite, with just enough of a lift to stop the black reading as an absence.</summary>
    public static ThemeDefinition SlateDark { get; } = ThemeDefinition.From(
        id: "slate-dark",
        name: "Slate Dark",
        family: "Slate",
        mode: ThemeMode.Dark,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Hexfield, Blur = 11 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFF171717u),
            SurfacePanel = new ThemeColour(0x32000000u),
            SurfaceRaised = new ThemeColour(0xFF232323u),
            SurfaceRaisedHover = new ThemeColour(0xFF2C2C2Cu),
            SurfaceCard = new ThemeColour(0xFF323232u),
            SurfaceDivider = new ThemeColour(0xFF151515u),
            SurfaceOverlay = new ThemeColour(0xFF1E1E1Eu),
            ConnectorStrip = new ThemeColour(0xFF242424u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x1FFFFFFFu),
            IconGroupEdge = new ThemeColour(0x4DFFFFFFu),
            Scrim = new ThemeColour(0xA6000000u),
            PipeShadow = new ThemeColour(0x32000000u),

            // Text
            TextPrimary = new ThemeColour(0xFFEDEDEDu),
            TextSecondary = new ThemeColour(0xFF9A9A9Au),
            TextFaint = new ThemeColour(0xFF6A6A6Au),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF5A5A5Au),
            StateRunning = new ThemeColour(0xFF4680FCu),
            StateComplete = new ThemeColour(0xFF62C98Fu),
            StateError = new ThemeColour(0xFFFC4646u),
            StateTimeout = new ThemeColour(0xFFFCAF62u),
            StateSkipped = new ThemeColour(0xFF7A7A7Au),
            StatePaused = new ThemeColour(0xFFC46AC4u),

            // Accent
            Accent = new ThemeColour(0xFF4680FCu),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF2AFC4Du),
            FlowVariableSurface = new ThemeColour(0xFF3F7C7Fu),
            FlowArtifact = new ThemeColour(0xFFFCC22Au),
            FlowArtifactSurface = new ThemeColour(0xFF4D7F3Fu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x2662C98Fu),
            DiffAddedText = new ThemeColour(0xFF9FE3BBu),
            DiffRemovedSurface = new ThemeColour(0x26FC4646u),
            DiffRemovedText = new ThemeColour(0xFFF4A0A0u),
            DiffContextText = new ThemeColour(0xFF8A8A8Au),

            // Ink
            InkNeutral = new ThemeColour(0xFFF4F4F4u),
            InkCyan = new ThemeColour(0xFF35D6E8u),
            InkMagenta = new ThemeColour(0xFFFF5FD2u),
            InkViolet = new ThemeColour(0xFF9B7BFFu),
            InkOrange = new ThemeColour(0xFFFF9A3Cu),
            InkHalo = new ThemeColour(0xA0000000u),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF62A8C9u),
            ValueDocument = new ThemeColour(0xFF62C98Fu),
            ValueBinary = new ThemeColour(0xFFC9A862u),
            ValueTabular = new ThemeColour(0xFFC46AC4u),
            ValuePlain = new ThemeColour(0xFF8FA7C9u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xC8000000u),
            BackdropBase = new ThemeColour(0xFF131416u),
            BackdropNear = new ThemeColour(0xFF2C2F35u),
            BackdropFar = new ThemeColour(0xFF191A1Du),
            BackdropGlow = new ThemeColour(0x33566E8Cu)
        });

    /// <summary>Slate Light. The same honeycomb in daylight. Panels are white frost over a tinted field rather than a grey wash over grey &#8212; that one change is what stops a light theme reading as muddy.</summary>
    public static ThemeDefinition SlateLight { get; } = ThemeDefinition.From(
        id: "slate-light",
        name: "Slate Light",
        family: "Slate",
        mode: ThemeMode.Light,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Hexfield, Blur = 11 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFFE9ECF2u),
            SurfacePanel = new ThemeColour(0xB8FFFFFFu),
            SurfaceRaised = new ThemeColour(0xFFFFFFFFu),
            SurfaceRaisedHover = new ThemeColour(0xFFEFF2F8u),
            SurfaceCard = new ThemeColour(0xFFFFFFFFu),
            SurfaceDivider = new ThemeColour(0xFFD3D9E4u),
            SurfaceOverlay = new ThemeColour(0xFAFFFFFFu),
            ConnectorStrip = new ThemeColour(0xFFEAEEF5u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x2A1B2A47u),
            IconGroupEdge = new ThemeColour(0x4D1B2A47u),
            Scrim = new ThemeColour(0x4D0C1424u),
            PipeShadow = new ThemeColour(0x1A0C1424u),

            // Text
            TextPrimary = new ThemeColour(0xFF11141Bu),
            TextSecondary = new ThemeColour(0xFF4E5666u),
            TextFaint = new ThemeColour(0xFF848C9Cu),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFFA6AEBCu),
            StateRunning = new ThemeColour(0xFF1D5FE8u),
            StateComplete = new ThemeColour(0xFF0E8A4Fu),
            StateError = new ThemeColour(0xFFD32020u),
            StateTimeout = new ThemeColour(0xFFB86A00u),
            StateSkipped = new ThemeColour(0xFF8A92A1u),
            StatePaused = new ThemeColour(0xFF8E33B4u),

            // Accent
            Accent = new ThemeColour(0xFF1D5FE8u),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF0BA13Cu),
            FlowVariableSurface = new ThemeColour(0xFF5FA8B0u),
            FlowArtifact = new ThemeColour(0xFFCE9200u),
            FlowArtifactSurface = new ThemeColour(0xFF6FAE5Cu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x300E8A4Fu),
            DiffAddedText = new ThemeColour(0xFF0A6438u),
            DiffRemovedSurface = new ThemeColour(0x30D32020u),
            DiffRemovedText = new ThemeColour(0xFFA31414u),
            DiffContextText = new ThemeColour(0xFF6B7383u),

            // Ink
            InkNeutral = new ThemeColour(0xFF14171Fu),
            InkCyan = new ThemeColour(0xFF0090A6u),
            InkMagenta = new ThemeColour(0xFFDC0B93u),
            InkViolet = new ThemeColour(0xFF6B3FE8u),
            InkOrange = new ThemeColour(0xFFE06A00u),
            InkHalo = new ThemeColour(0xB3FFFFFFu),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF2C7A9Eu),
            ValueDocument = new ThemeColour(0xFF2E8C5Cu),
            ValueBinary = new ThemeColour(0xFF97701Fu),
            ValueTabular = new ThemeColour(0xFF8E3E92u),
            ValuePlain = new ThemeColour(0xFF556F94u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xC8FFFFFFu),
            BackdropBase = new ThemeColour(0xFFF7F9FDu),
            BackdropNear = new ThemeColour(0xFFCCDDF4u),
            BackdropFar = new ThemeColour(0xFFEEF3FBu),
            BackdropGlow = new ThemeColour(0x6689BEFFu)
        });

    /// <summary>Ember Dark. Rings around an ember sitting off the top-right corner. Copper accent, so timeout moves to yellow and paused to violet-blue &#8212; six hues that still separate.</summary>
    public static ThemeDefinition EmberDark { get; } = ThemeDefinition.From(
        id: "ember-dark",
        name: "Ember Dark",
        family: "Ember",
        mode: ThemeMode.Dark,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Orbits, Blur = 14 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFF1A1512u),
            SurfacePanel = new ThemeColour(0x32000000u),
            SurfaceRaised = new ThemeColour(0xFF261F1Au),
            SurfaceRaisedHover = new ThemeColour(0xFF2F2721u),
            SurfaceCard = new ThemeColour(0xFF352C25u),
            SurfaceDivider = new ThemeColour(0xFF171310u),
            SurfaceOverlay = new ThemeColour(0xFF201A16u),
            ConnectorStrip = new ThemeColour(0xFF271F19u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x1FFFE9D6u),
            IconGroupEdge = new ThemeColour(0x4DFFE9D6u),
            Scrim = new ThemeColour(0xA6120D0Au),
            PipeShadow = new ThemeColour(0x32000000u),

            // Text
            TextPrimary = new ThemeColour(0xFFF2E8DFu),
            TextSecondary = new ThemeColour(0xFFA79A8Eu),
            TextFaint = new ThemeColour(0xFF75695Fu),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF63574Eu),
            StateRunning = new ThemeColour(0xFF5B90F5u),
            StateComplete = new ThemeColour(0xFF6FC98Au),
            StateError = new ThemeColour(0xFFF25050u),
            StateTimeout = new ThemeColour(0xFFE8C547u),
            StateSkipped = new ThemeColour(0xFF857569u),
            StatePaused = new ThemeColour(0xFF9B84E8u),

            // Accent
            Accent = new ThemeColour(0xFFE07A3Fu),
            AccentInk = new ThemeColour(0xFF1F1206u),

            // Flow
            FlowVariable = new ThemeColour(0xFF3EE07Au),
            FlowVariableSurface = new ThemeColour(0xFF4B7C6Eu),
            FlowArtifact = new ThemeColour(0xFFF5B733u),
            FlowArtifactSurface = new ThemeColour(0xFF7C6B3Fu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x266FC98Au),
            DiffAddedText = new ThemeColour(0xFFA6E2BCu),
            DiffRemovedSurface = new ThemeColour(0x26F25050u),
            DiffRemovedText = new ThemeColour(0xFFF6ABA5u),
            DiffContextText = new ThemeColour(0xFF95877Bu),

            // Ink
            InkNeutral = new ThemeColour(0xFFF7EFE6u),
            InkCyan = new ThemeColour(0xFF3FD2C9u),
            InkMagenta = new ThemeColour(0xFFFF6BC2u),
            InkViolet = new ThemeColour(0xFFA98CFFu),
            InkOrange = new ThemeColour(0xFFFFA85Cu),
            InkHalo = new ThemeColour(0xA01A100Au),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF62A8C9u),
            ValueDocument = new ThemeColour(0xFF62C98Fu),
            ValueBinary = new ThemeColour(0xFFC9A862u),
            ValueTabular = new ThemeColour(0xFFC46AC4u),
            ValuePlain = new ThemeColour(0xFF8FA7C9u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xC80D0906u),
            BackdropBase = new ThemeColour(0xFF150F0Cu),
            BackdropNear = new ThemeColour(0xFF54301Au),
            BackdropFar = new ThemeColour(0xFF1C1512u),
            BackdropGlow = new ThemeColour(0x8CFF7A2Eu)
        });

    /// <summary>Ember Light. The same rings in terracotta on cream. The lowest-contrast pair here on purpose &#8212; for a screen you read all afternoon &#8212; but the ink stays properly black.</summary>
    public static ThemeDefinition EmberLight { get; } = ThemeDefinition.From(
        id: "ember-light",
        name: "Ember Light",
        family: "Ember",
        mode: ThemeMode.Light,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Orbits, Blur = 14 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFFF3E9D9u),
            SurfacePanel = new ThemeColour(0xBAFFFCF6u),
            SurfaceRaised = new ThemeColour(0xFFFFFDF9u),
            SurfaceRaisedHover = new ThemeColour(0xFFF8F0E4u),
            SurfaceCard = new ThemeColour(0xFFFFFDF9u),
            SurfaceDivider = new ThemeColour(0xFFE2D5C0u),
            SurfaceOverlay = new ThemeColour(0xFAFFFCF7u),
            ConnectorStrip = new ThemeColour(0xFFF2E8D8u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x2A6B4A22u),
            IconGroupEdge = new ThemeColour(0x4D6B4A22u),
            Scrim = new ThemeColour(0x4D2B1B08u),
            PipeShadow = new ThemeColour(0x1A2B1B08u),

            // Text
            TextPrimary = new ThemeColour(0xFF1F1509u),
            TextSecondary = new ThemeColour(0xFF5F5241u),
            TextFaint = new ThemeColour(0xFF938676u),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFFB5A794u),
            StateRunning = new ThemeColour(0xFF2456D6u),
            StateComplete = new ThemeColour(0xFF12783Fu),
            StateError = new ThemeColour(0xFFC72424u),
            StateTimeout = new ThemeColour(0xFF9C6C00u),
            StateSkipped = new ThemeColour(0xFF938676u),
            StatePaused = new ThemeColour(0xFF6B3BC8u),

            // Accent
            Accent = new ThemeColour(0xFFC2551Au),
            AccentInk = new ThemeColour(0xFFFFF7F0u),

            // Flow
            FlowVariable = new ThemeColour(0xFF0B8A33u),
            FlowVariableSurface = new ThemeColour(0xFF74A594u),
            FlowArtifact = new ThemeColour(0xFFB8880Au),
            FlowArtifactSurface = new ThemeColour(0xFF9C9450u),

            // Diff
            DiffAddedSurface = new ThemeColour(0x3012783Fu),
            DiffAddedText = new ThemeColour(0xFF0C5C2Eu),
            DiffRemovedSurface = new ThemeColour(0x30C72424u),
            DiffRemovedText = new ThemeColour(0xFF991616u),
            DiffContextText = new ThemeColour(0xFF6E6252u),

            // Ink
            InkNeutral = new ThemeColour(0xFF1F1509u),
            InkCyan = new ThemeColour(0xFF00857Eu),
            InkMagenta = new ThemeColour(0xFFC30F79u),
            InkViolet = new ThemeColour(0xFF5A2ECCu),
            InkOrange = new ThemeColour(0xFFC85400u),
            InkHalo = new ThemeColour(0xB3FFF8ECu),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF2C7A9Eu),
            ValueDocument = new ThemeColour(0xFF2E8C5Cu),
            ValueBinary = new ThemeColour(0xFF97701Fu),
            ValueTabular = new ThemeColour(0xFF8E3E92u),
            ValuePlain = new ThemeColour(0xFF556F94u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xC8FFFAF2u),
            BackdropBase = new ThemeColour(0xFFFDF7ECu),
            BackdropNear = new ThemeColour(0xFFF2CE94u),
            BackdropFar = new ThemeColour(0xFFFBF0DFu),
            BackdropGlow = new ThemeColour(0x8CFFA23Cu)
        });

    /// <summary>Tide Dark. Discs at depth over deep navy &#8212; the softest backdrop of the eight. The accent is teal, which finally frees azure to mean one thing only: this step is running.</summary>
    public static ThemeDefinition TideDark { get; } = ThemeDefinition.From(
        id: "tide-dark",
        name: "Tide Dark",
        family: "Tide",
        mode: ThemeMode.Dark,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Scatter, Blur = 15 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFF10161Cu),
            SurfacePanel = new ThemeColour(0x32000814u),
            SurfaceRaised = new ThemeColour(0xFF1A242Du),
            SurfaceRaisedHover = new ThemeColour(0xFF223039u),
            SurfaceCard = new ThemeColour(0xFF27343Eu),
            SurfaceDivider = new ThemeColour(0xFF0D1318u),
            SurfaceOverlay = new ThemeColour(0xFF15202Au),
            ConnectorStrip = new ThemeColour(0xFF1B252Eu),

            // Edges and depth
            PanelEdge = new ThemeColour(0x1FD6ECFFu),
            IconGroupEdge = new ThemeColour(0x4DD6ECFFu),
            Scrim = new ThemeColour(0xA6060C12u),
            PipeShadow = new ThemeColour(0x32000000u),

            // Text
            TextPrimary = new ThemeColour(0xFFE6EEF4u),
            TextSecondary = new ThemeColour(0xFF90A2B0u),
            TextFaint = new ThemeColour(0xFF62737Fu),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF4E5F6Bu),
            StateRunning = new ThemeColour(0xFF5691FFu),
            StateComplete = new ThemeColour(0xFF57CF9Cu),
            StateError = new ThemeColour(0xFFFF5A63u),
            StateTimeout = new ThemeColour(0xFFFFB65Cu),
            StateSkipped = new ThemeColour(0xFF6E8090u),
            StatePaused = new ThemeColour(0xFFB57BE8u),

            // Accent
            Accent = new ThemeColour(0xFF2CC5C8u),
            AccentInk = new ThemeColour(0xFF04211Fu),

            // Flow
            FlowVariable = new ThemeColour(0xFF34F07Au),
            FlowVariableSurface = new ThemeColour(0xFF3C7480u),
            FlowArtifact = new ThemeColour(0xFFFFC44Au),
            FlowArtifactSurface = new ThemeColour(0xFF4A7A5Eu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x2657CF9Cu),
            DiffAddedText = new ThemeColour(0xFF98E6C4u),
            DiffRemovedSurface = new ThemeColour(0x26FF5A63u),
            DiffRemovedText = new ThemeColour(0xFFFFAAAEu),
            DiffContextText = new ThemeColour(0xFF7E8F9Cu),

            // Ink
            InkNeutral = new ThemeColour(0xFFEFF6FBu),
            InkCyan = new ThemeColour(0xFF44E2F2u),
            InkMagenta = new ThemeColour(0xFFFF6ACEu),
            InkViolet = new ThemeColour(0xFF9E8BFFu),
            InkOrange = new ThemeColour(0xFFFFA24Au),
            InkHalo = new ThemeColour(0xA0040A10u),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF62A8C9u),
            ValueDocument = new ThemeColour(0xFF62C98Fu),
            ValueBinary = new ThemeColour(0xFFC9A862u),
            ValueTabular = new ThemeColour(0xFFC46AC4u),
            ValuePlain = new ThemeColour(0xFF8FA7C9u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xC8040A12u),
            BackdropBase = new ThemeColour(0xFF080E16u),
            BackdropNear = new ThemeColour(0xFF1B5E6Eu),
            BackdropFar = new ThemeColour(0xFF122340u),
            BackdropGlow = new ThemeColour(0xA62CD8C8u)
        });

    /// <summary>Tide Light. The same discs in aqua on porcelain. The crispest of the light themes &#8212; the one to demo on, and the one that survives a projector.</summary>
    public static ThemeDefinition TideLight { get; } = ThemeDefinition.From(
        id: "tide-light",
        name: "Tide Light",
        family: "Tide",
        mode: ThemeMode.Light,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Scatter, Blur = 15 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFFE2ECF4u),
            SurfacePanel = new ThemeColour(0xB8FFFFFFu),
            SurfaceRaised = new ThemeColour(0xFFFFFFFFu),
            SurfaceRaisedHover = new ThemeColour(0xFFEDF4FAu),
            SurfaceCard = new ThemeColour(0xFFFFFFFFu),
            SurfaceDivider = new ThemeColour(0xFFCBDBE7u),
            SurfaceOverlay = new ThemeColour(0xFAFCFEFFu),
            ConnectorStrip = new ThemeColour(0xFFE4EEF6u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x2A0A3040u),
            IconGroupEdge = new ThemeColour(0x4D0A3040u),
            Scrim = new ThemeColour(0x4D04202Cu),
            PipeShadow = new ThemeColour(0x1A04202Cu),

            // Text
            TextPrimary = new ThemeColour(0xFF07181Fu),
            TextSecondary = new ThemeColour(0xFF48606Eu),
            TextFaint = new ThemeColour(0xFF7A93A2u),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF9BB0BEu),
            StateRunning = new ThemeColour(0xFF1560DEu),
            StateComplete = new ThemeColour(0xFF067A4Bu),
            StateError = new ThemeColour(0xFFC8202Au),
            StateTimeout = new ThemeColour(0xFFAD6A00u),
            StateSkipped = new ThemeColour(0xFF7A93A2u),
            StatePaused = new ThemeColour(0xFF7A2CC4u),

            // Accent
            Accent = new ThemeColour(0xFF00808Eu),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF058A3Au),
            FlowVariableSurface = new ThemeColour(0xFF5FA4B2u),
            FlowArtifact = new ThemeColour(0xFFB58400u),
            FlowArtifactSurface = new ThemeColour(0xFF5FA487u),

            // Diff
            DiffAddedSurface = new ThemeColour(0x30067A4Bu),
            DiffAddedText = new ThemeColour(0xFF045C38u),
            DiffRemovedSurface = new ThemeColour(0x30C8202Au),
            DiffRemovedText = new ThemeColour(0xFF97161Eu),
            DiffContextText = new ThemeColour(0xFF5E7788u),

            // Ink
            InkNeutral = new ThemeColour(0xFF07181Fu),
            InkCyan = new ThemeColour(0xFF00808Eu),
            InkMagenta = new ThemeColour(0xFFC30C86u),
            InkViolet = new ThemeColour(0xFF5533CCu),
            InkOrange = new ThemeColour(0xFFC85C00u),
            InkHalo = new ThemeColour(0xB3FFFFFFu),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF2C7A9Eu),
            ValueDocument = new ThemeColour(0xFF2E8C5Cu),
            ValueBinary = new ThemeColour(0xFF97701Fu),
            ValueTabular = new ThemeColour(0xFF8E3E92u),
            ValuePlain = new ThemeColour(0xFF556F94u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xC8F6FBFFu),
            BackdropBase = new ThemeColour(0xFFF4FAFDu),
            BackdropNear = new ThemeColour(0xFFA8DCE6u),
            BackdropFar = new ThemeColour(0xFFDCEDF8u),
            BackdropGlow = new ThemeColour(0x8C48C8D8u)
        });

    /// <summary>Glass Dark. Smoked glass. Nothing is painted behind the window &#8212; the acrylic tint and whatever is on the desktop are the background. The tint carries seventy percent, which is what it takes for white text to hold over an unknown wallpaper; the thirty that gets through is the whole effect.</summary>
    public static ThemeDefinition GlassDark { get; } = ThemeDefinition.From(
        id: "glass-dark",
        name: "Glass Dark",
        family: "Glass",
        mode: ThemeMode.Dark,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Clear, Blur = 0 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0x8C0E1014u),
            SurfacePanel = new ThemeColour(0x4D0A0C10u),
            SurfaceRaised = new ThemeColour(0xB21A1E26u),
            SurfaceRaisedHover = new ThemeColour(0xC6242A35u),
            SurfaceCard = new ThemeColour(0xD920252Fu),
            SurfaceDivider = new ThemeColour(0x66000000u),
            SurfaceOverlay = new ThemeColour(0xF0161A22u),
            ConnectorStrip = new ThemeColour(0xB2141820u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x4DFFFFFFu),
            IconGroupEdge = new ThemeColour(0x80FFFFFFu),
            Scrim = new ThemeColour(0xB3000000u),
            PipeShadow = new ThemeColour(0x59000000u),

            // Text
            TextPrimary = new ThemeColour(0xFFFFFFFFu),
            TextSecondary = new ThemeColour(0xFFC6CCD6u),
            TextFaint = new ThemeColour(0xFF9AA3B0u),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF7E8996u),
            StateRunning = new ThemeColour(0xFF6FA3FFu),
            StateComplete = new ThemeColour(0xFF6FDCA5u),
            StateError = new ThemeColour(0xFFFF7070u),
            StateTimeout = new ThemeColour(0xFFFFC271u),
            StateSkipped = new ThemeColour(0xFF9AA3B0u),
            StatePaused = new ThemeColour(0xFFD08BEAu),

            // Accent
            Accent = new ThemeColour(0xFF6FA3FFu),
            AccentInk = new ThemeColour(0xFF0A1020u),

            // Flow
            FlowVariable = new ThemeColour(0xFF54FF8Cu),
            FlowVariableSurface = new ThemeColour(0xFF4C8A8Eu),
            FlowArtifact = new ThemeColour(0xFFFFD05Cu),
            FlowArtifactSurface = new ThemeColour(0xFF5C8E4Cu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x3D6FDCA5u),
            DiffAddedText = new ThemeColour(0xFFB2EFCEu),
            DiffRemovedSurface = new ThemeColour(0x3DFF7070u),
            DiffRemovedText = new ThemeColour(0xFFFFBABAu),
            DiffContextText = new ThemeColour(0xFFACB5C1u),

            // Ink
            InkNeutral = new ThemeColour(0xFFFFFFFFu),
            InkCyan = new ThemeColour(0xFF4FE3F2u),
            InkMagenta = new ThemeColour(0xFFFF7BD6u),
            InkViolet = new ThemeColour(0xFFAE97FFu),
            InkOrange = new ThemeColour(0xFFFFAE5Cu),
            InkHalo = new ThemeColour(0xB3000000u),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF62A8C9u),
            ValueDocument = new ThemeColour(0xFF62C98Fu),
            ValueBinary = new ThemeColour(0xFFC9A862u),
            ValueTabular = new ThemeColour(0xFFC46AC4u),
            ValuePlain = new ThemeColour(0xFF8FA7C9u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xB3101318u),
            BackdropBase = new ThemeColour(0x00000000u),
            BackdropNear = new ThemeColour(0x4D2A3140u),
            BackdropFar = new ThemeColour(0x33141820u),
            BackdropGlow = new ThemeColour(0x4C6FA3FFu)
        });

    /// <summary>Glass Light. Frosted glass. The frost is thick enough that a dark desktop does not swallow the text &#8212; and the harder of the two, because dark text over a bright wallpaper needs every lifecycle colour pushed down until it reads as ink.</summary>
    public static ThemeDefinition GlassLight { get; } = ThemeDefinition.From(
        id: "glass-light",
        name: "Glass Light",
        family: "Glass",
        mode: ThemeMode.Light,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Clear, Blur = 0 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0x8CEFF3F9u),
            SurfacePanel = new ThemeColour(0x4DFFFFFFu),
            SurfaceRaised = new ThemeColour(0xD9FFFFFFu),
            SurfaceRaisedHover = new ThemeColour(0xE6F0F4FAu),
            SurfaceCard = new ThemeColour(0xE6FFFFFFu),
            SurfaceDivider = new ThemeColour(0x40203044u),
            SurfaceOverlay = new ThemeColour(0xF5FFFFFFu),
            ConnectorStrip = new ThemeColour(0xCCE8EDF5u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x59203044u),
            IconGroupEdge = new ThemeColour(0x80203044u),
            Scrim = new ThemeColour(0x59000000u),
            PipeShadow = new ThemeColour(0x40000000u),

            // Text
            TextPrimary = new ThemeColour(0xFF080B12u),
            TextSecondary = new ThemeColour(0xFF39414Fu),
            TextFaint = new ThemeColour(0xFF67707Eu),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF7A8290u),
            StateRunning = new ThemeColour(0xFF1348D6u),
            StateComplete = new ThemeColour(0xFF07713Fu),
            StateError = new ThemeColour(0xFFB81717u),
            StateTimeout = new ThemeColour(0xFF945400u),
            StateSkipped = new ThemeColour(0xFF67707Eu),
            StatePaused = new ThemeColour(0xFF7326A0u),

            // Accent
            Accent = new ThemeColour(0xFF1348D6u),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF067A2Cu),
            FlowVariableSurface = new ThemeColour(0xFF548E96u),
            FlowArtifact = new ThemeColour(0xFF9C7000u),
            FlowArtifactSurface = new ThemeColour(0xFF5E9450u),

            // Diff
            DiffAddedSurface = new ThemeColour(0x3D07713Fu),
            DiffAddedText = new ThemeColour(0xFF05512Cu),
            DiffRemovedSurface = new ThemeColour(0x3DB81717u),
            DiffRemovedText = new ThemeColour(0xFF8C1010u),
            DiffContextText = new ThemeColour(0xFF505869u),

            // Ink
            InkNeutral = new ThemeColour(0xFF080B12u),
            InkCyan = new ThemeColour(0xFF00707Eu),
            InkMagenta = new ThemeColour(0xFFAD0A74u),
            InkViolet = new ThemeColour(0xFF4A2AB8u),
            InkOrange = new ThemeColour(0xFFB04C00u),
            InkHalo = new ThemeColour(0xCCFFFFFFu),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF2C7A9Eu),
            ValueDocument = new ThemeColour(0xFF2E8C5Cu),
            ValueBinary = new ThemeColour(0xFF97701Fu),
            ValueTabular = new ThemeColour(0xFF8E3E92u),
            ValuePlain = new ThemeColour(0xFF556F94u),

            // Window and backdrop
            WindowTint = new ThemeColour(0xBFF4F7FCu),
            BackdropBase = new ThemeColour(0x00FFFFFFu),
            BackdropNear = new ThemeColour(0x4DCCDDF4u),
            BackdropFar = new ThemeColour(0x33EEF3FBu),
            BackdropGlow = new ThemeColour(0x4C89BEFFu)
        });

    /// <summary>Contrast Dark. Not a style &#8212; a mode. Flat black, real borders instead of hairlines, no backdrop at all. It is the theme that proves the backdrop has to belong to the theme.</summary>
    public static ThemeDefinition ContrastDark { get; } = ThemeDefinition.From(
        id: "contrast-dark",
        name: "Contrast Dark",
        family: "Contrast",
        mode: ThemeMode.Dark,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Flat, Blur = 0 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFF000000u),
            SurfacePanel = new ThemeColour(0xFF000000u),
            SurfaceRaised = new ThemeColour(0xFF141414u),
            SurfaceRaisedHover = new ThemeColour(0xFF242424u),
            SurfaceCard = new ThemeColour(0xFF1C1C1Cu),
            SurfaceDivider = new ThemeColour(0xFF3A3A3Au),
            SurfaceOverlay = new ThemeColour(0xFF0A0A0Au),
            ConnectorStrip = new ThemeColour(0xFF101010u),

            // Edges and depth
            PanelEdge = new ThemeColour(0xFF6E6E6Eu),
            IconGroupEdge = new ThemeColour(0xFF8A8A8Au),
            Scrim = new ThemeColour(0xD9000000u),
            PipeShadow = new ThemeColour(0x00000000u),

            // Text
            TextPrimary = new ThemeColour(0xFFFFFFFFu),
            TextSecondary = new ThemeColour(0xFFD4D4D4u),
            TextFaint = new ThemeColour(0xFFA8A8A8u),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF9A9A9Au),
            StateRunning = new ThemeColour(0xFF7AAEFFu),
            StateComplete = new ThemeColour(0xFF56E39Fu),
            StateError = new ThemeColour(0xFFFF7B7Bu),
            StateTimeout = new ThemeColour(0xFFFFD166u),
            StateSkipped = new ThemeColour(0xFFB4B4B4u),
            StatePaused = new ThemeColour(0xFFE29BFFu),

            // Accent
            Accent = new ThemeColour(0xFF7AAEFFu),
            AccentInk = new ThemeColour(0xFF000000u),

            // Flow
            FlowVariable = new ThemeColour(0xFF5CFF87u),
            FlowVariableSurface = new ThemeColour(0xFF4E8C90u),
            FlowArtifact = new ThemeColour(0xFFFFD24Du),
            FlowArtifactSurface = new ThemeColour(0xFF5C9050u),

            // Diff
            DiffAddedSurface = new ThemeColour(0x4056E39Fu),
            DiffAddedText = new ThemeColour(0xFFB8F5D6u),
            DiffRemovedSurface = new ThemeColour(0x40FF7B7Bu),
            DiffRemovedText = new ThemeColour(0xFFFFC2C2u),
            DiffContextText = new ThemeColour(0xFFC0C0C0u),

            // Ink
            InkNeutral = new ThemeColour(0xFFFFFFFFu),
            InkCyan = new ThemeColour(0xFF5EE8F5u),
            InkMagenta = new ThemeColour(0xFFFF8AD9u),
            InkViolet = new ThemeColour(0xFFB9A3FFu),
            InkOrange = new ThemeColour(0xFFFFB25Eu),
            InkHalo = new ThemeColour(0xD9000000u),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF8FCBE4u),
            ValueDocument = new ThemeColour(0xFF7FE0ACu),
            ValueBinary = new ThemeColour(0xFFE0C077u),
            ValueTabular = new ThemeColour(0xFFDE94DEu),
            ValuePlain = new ThemeColour(0xFFAFC2DEu),

            // Window and backdrop
            WindowTint = new ThemeColour(0xFF000000u),
            BackdropBase = new ThemeColour(0xFF000000u),
            BackdropNear = new ThemeColour(0xFF000000u),
            BackdropFar = new ThemeColour(0xFF000000u),
            BackdropGlow = new ThemeColour(0x00000000u)
        });

    /// <summary>Contrast Light. The same mode on white. Every lifecycle colour is darkened until it reads as text rather than as a highlight, and nothing is translucent.</summary>
    public static ThemeDefinition ContrastLight { get; } = ThemeDefinition.From(
        id: "contrast-light",
        name: "Contrast Light",
        family: "Contrast",
        mode: ThemeMode.Light,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Flat, Blur = 0 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFFFFFFFFu),
            SurfacePanel = new ThemeColour(0xFFFFFFFFu),
            SurfaceRaised = new ThemeColour(0xFFF2F2F2u),
            SurfaceRaisedHover = new ThemeColour(0xFFE4E4E4u),
            SurfaceCard = new ThemeColour(0xFFFFFFFFu),
            SurfaceDivider = new ThemeColour(0xFF9A9A9Au),
            SurfaceOverlay = new ThemeColour(0xFFFFFFFFu),
            ConnectorStrip = new ThemeColour(0xFFEDEDEDu),

            // Edges and depth
            PanelEdge = new ThemeColour(0xFF4A4A4Au),
            IconGroupEdge = new ThemeColour(0xFF2E2E2Eu),
            Scrim = new ThemeColour(0x73000000u),
            PipeShadow = new ThemeColour(0x00000000u),

            // Text
            TextPrimary = new ThemeColour(0xFF000000u),
            TextSecondary = new ThemeColour(0xFF2E2E2Eu),
            TextFaint = new ThemeColour(0xFF565656u),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF5C5C5Cu),
            StateRunning = new ThemeColour(0xFF0B4FC4u),
            StateComplete = new ThemeColour(0xFF056034u),
            StateError = new ThemeColour(0xFFA80D14u),
            StateTimeout = new ThemeColour(0xFF7A4E00u),
            StateSkipped = new ThemeColour(0xFF565656u),
            StatePaused = new ThemeColour(0xFF5B1E96u),

            // Accent
            Accent = new ThemeColour(0xFF0B4FC4u),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF05702Cu),
            FlowVariableSurface = new ThemeColour(0xFF4C7C80u),
            FlowArtifact = new ThemeColour(0xFF7A5A00u),
            FlowArtifactSurface = new ThemeColour(0xFF4E7C42u),

            // Diff
            DiffAddedSurface = new ThemeColour(0x33056034u),
            DiffAddedText = new ThemeColour(0xFF034625u),
            DiffRemovedSurface = new ThemeColour(0x33A80D14u),
            DiffRemovedText = new ThemeColour(0xFF7A0910u),
            DiffContextText = new ThemeColour(0xFF3A3A3Au),

            // Ink
            InkNeutral = new ThemeColour(0xFF000000u),
            InkCyan = new ThemeColour(0xFF00646Eu),
            InkMagenta = new ThemeColour(0xFF9C0A6Au),
            InkViolet = new ThemeColour(0xFF44239Eu),
            InkOrange = new ThemeColour(0xFF9A4700u),
            InkHalo = new ThemeColour(0xD9FFFFFFu),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF17566Fu),
            ValueDocument = new ThemeColour(0xFF0F6B3Cu),
            ValueBinary = new ThemeColour(0xFF6B4E08u),
            ValueTabular = new ThemeColour(0xFF6B2270u),
            ValuePlain = new ThemeColour(0xFF33517Au),

            // Window and backdrop
            WindowTint = new ThemeColour(0xFFFFFFFFu),
            BackdropBase = new ThemeColour(0xFFFFFFFFu),
            BackdropNear = new ThemeColour(0xFFFFFFFFu),
            BackdropFar = new ThemeColour(0xFFFFFFFFu),
            BackdropGlow = new ThemeColour(0x00000000u)
        });

    /// <summary>
    /// Origin Dark. The tool as it looked before it had themes.
    /// </summary>
    /// <remarks>
    /// Flat graphite and the azure accent, which Slate Dark also carries — the difference is the
    /// backdrop. This is the original one: twelve layered ridges in the near-black greys they were
    /// actually drawn in, from <c>#161616</c> at the back to <c>#262626</c> at the front, rather than
    /// the hex field that replaced them. Softened a little, because the painter's canvas is enlarged to
    /// fill the window and a crisp edge does not survive that.
    /// </remarks>
    public static ThemeDefinition OriginDark { get; } = ThemeDefinition.From(
        id: "origin-dark",
        name: "Origin Dark",
        family: "Origin",
        mode: ThemeMode.Dark,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Origin, Blur = 80 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFF171717u),
            SurfacePanel = new ThemeColour(0x32000000u),
            SurfaceRaised = new ThemeColour(0xFF232323u),
            SurfaceRaisedHover = new ThemeColour(0xFF2C2C2Cu),
            SurfaceCard = new ThemeColour(0xFF323232u),
            SurfaceDivider = new ThemeColour(0xFF151515u),
            SurfaceOverlay = new ThemeColour(0xFF1E1E1Eu),
            ConnectorStrip = new ThemeColour(0xFF242424u),

            // Edges and depth
            PanelEdge = new ThemeColour(0x1FFFFFFFu),
            IconGroupEdge = new ThemeColour(0x4DFFFFFFu),
            Scrim = new ThemeColour(0xA6000000u),
            PipeShadow = new ThemeColour(0x32000000u),

            // Text
            TextPrimary = new ThemeColour(0xFFEDEDEDu),
            TextSecondary = new ThemeColour(0xFF9A9A9Au),
            TextFaint = new ThemeColour(0xFF6A6A6Au),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFF5A5A5Au),
            StateRunning = new ThemeColour(0xFF4680FCu),
            StateComplete = new ThemeColour(0xFF62C98Fu),
            StateError = new ThemeColour(0xFFFC4646u),
            StateTimeout = new ThemeColour(0xFFFCAF62u),
            StateSkipped = new ThemeColour(0xFF7A7A7Au),
            StatePaused = new ThemeColour(0xFFC46AC4u),

            // Accent
            Accent = new ThemeColour(0xFF4680FCu),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF2AFC4Du),
            FlowVariableSurface = new ThemeColour(0xFF3F7C7Fu),
            FlowArtifact = new ThemeColour(0xFFFCC22Au),
            FlowArtifactSurface = new ThemeColour(0xFF4D7F3Fu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x2662C98Fu),
            DiffAddedText = new ThemeColour(0xFF9FE3BBu),
            DiffRemovedSurface = new ThemeColour(0x26FC4646u),
            DiffRemovedText = new ThemeColour(0xFFF4A0A0u),
            DiffContextText = new ThemeColour(0xFF8A8A8Au),

            // Ink
            InkNeutral = new ThemeColour(0xFFF4F4F4u),
            InkCyan = new ThemeColour(0xFF35D6E8u),
            InkMagenta = new ThemeColour(0xFFFF5FD2u),
            InkViolet = new ThemeColour(0xFF9B7BFFu),
            InkOrange = new ThemeColour(0xFFFF9A3Cu),
            InkHalo = new ThemeColour(0xA0000000u),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF62A8C9u),
            ValueDocument = new ThemeColour(0xFF62C98Fu),
            ValueBinary = new ThemeColour(0xFFC9A862u),
            ValueTabular = new ThemeColour(0xFFC46AC4u),
            ValuePlain = new ThemeColour(0xFF8FA7C9u),

            // Window and backdrop
            // Opaque, because the ridges cover the window. This is the tool as it was once the background
            // went in, which is the point at which the blur behind it stopped being visible at all.
            WindowTint = new ThemeColour(0xFF141414u),
            BackdropBase = new ThemeColour(0xFF141414u),
            BackdropFar = new ThemeColour(0xFF262626u),
            BackdropNear = new ThemeColour(0xFF161616u),
            BackdropGlow = new ThemeColour(0x00000000u)
        });

    /// <summary>
    /// Origin Light. The same flat monochrome in daylight.
    /// </summary>
    /// <remarks>
    /// Paper greys rather than tinted ones, so it stays as colourless as its dark half — that is the
    /// whole of what makes it the counterpart rather than another Slate. The azure accent carries
    /// across, darkened enough to hold on white, and the ridges become the pale bands the greys invert
    /// to. No glow: the original had none, and one here would be the only warm thing in the window.
    /// </remarks>
    public static ThemeDefinition OriginLight { get; } = ThemeDefinition.From(
        id: "origin-light",
        name: "Origin Light",
        family: "Origin",
        mode: ThemeMode.Light,
        backdrop: new ThemeBackdrop { Recipe = BackdropRecipe.Origin, Blur = 80 },
        palette: new ThemePalette
        {
            // Surfaces
            SurfaceSunken = new ThemeColour(0xFFECECECu),
            SurfacePanel = new ThemeColour(0xB8FFFFFFu),
            SurfaceRaised = new ThemeColour(0xFFFFFFFFu),
            SurfaceRaisedHover = new ThemeColour(0xFFF1F1F1u),
            SurfaceCard = new ThemeColour(0xFFFFFFFFu),
            SurfaceDivider = new ThemeColour(0xFFD8D8D8u),
            SurfaceOverlay = new ThemeColour(0xFAFFFFFFu),
            ConnectorStrip = new ThemeColour(0xFFEDEDEDu),

            // Edges and depth
            PanelEdge = new ThemeColour(0x2A1A1A1Au),
            IconGroupEdge = new ThemeColour(0x4D1A1A1Au),
            Scrim = new ThemeColour(0x4D141414u),
            PipeShadow = new ThemeColour(0x1A141414u),

            // Text
            TextPrimary = new ThemeColour(0xFF141414u),
            TextSecondary = new ThemeColour(0xFF565656u),
            TextFaint = new ThemeColour(0xFF8A8A8Au),

            // Lifecycle
            StateNotRun = new ThemeColour(0xFFAAAAAAu),
            StateRunning = new ThemeColour(0xFF1D5FE8u),
            StateComplete = new ThemeColour(0xFF0E8A4Fu),
            StateError = new ThemeColour(0xFFD32020u),
            StateTimeout = new ThemeColour(0xFFB86A00u),
            StateSkipped = new ThemeColour(0xFF8E8E8Eu),
            StatePaused = new ThemeColour(0xFF8E33B4u),

            // Accent
            Accent = new ThemeColour(0xFF1D5FE8u),
            AccentInk = new ThemeColour(0xFFFFFFFFu),

            // Flow
            FlowVariable = new ThemeColour(0xFF0BA13Cu),
            FlowVariableSurface = new ThemeColour(0xFF5FA8B0u),
            FlowArtifact = new ThemeColour(0xFFCE9200u),
            FlowArtifactSurface = new ThemeColour(0xFF6FAE5Cu),

            // Diff
            DiffAddedSurface = new ThemeColour(0x300E8A4Fu),
            DiffAddedText = new ThemeColour(0xFF0A6438u),
            DiffRemovedSurface = new ThemeColour(0x30D32020u),
            DiffRemovedText = new ThemeColour(0xFFA31414u),
            DiffContextText = new ThemeColour(0xFF6E6E6Eu),

            // Ink
            InkNeutral = new ThemeColour(0xFF141414u),
            InkCyan = new ThemeColour(0xFF0090A6u),
            InkMagenta = new ThemeColour(0xFFDC0B93u),
            InkViolet = new ThemeColour(0xFF6B3FE8u),
            InkOrange = new ThemeColour(0xFFE06A00u),
            InkHalo = new ThemeColour(0xB3FFFFFFu),

            // Value kinds
            ValueRelational = new ThemeColour(0xFF2C7A9Eu),
            ValueDocument = new ThemeColour(0xFF2E8C5Cu),
            ValueBinary = new ThemeColour(0xFF97701Fu),
            ValueTabular = new ThemeColour(0xFF8E3E92u),
            ValuePlain = new ThemeColour(0xFF5E5E5Eu),

            // Window and backdrop
            WindowTint = new ThemeColour(0xFFF4F4F4u),
            BackdropBase = new ThemeColour(0xFFF6F6F6u),
            BackdropFar = new ThemeColour(0xFFE2E2E2u),
            BackdropNear = new ThemeColour(0xFFFAFAFAu),
            BackdropGlow = new ThemeColour(0x00000000u)
        });

    /// <summary>Every built-in, in the order the picker shows them.</summary>
    public static ImmutableArray<ThemeDefinition> All { get; } =
    [
        SlateDark,
        SlateLight,
        EmberDark,
        EmberLight,
        TideDark,
        TideLight,
        GlassDark,
        GlassLight,
        OriginDark,
        OriginLight,
        ContrastDark,
        ContrastLight
    ];

    /// <summary>The built-ins by id, which is how a settings file and a theme file both name one.</summary>
    public static ImmutableDictionary<string, ThemeDefinition> ById { get; } =
        All.ToImmutableDictionary(theme => theme.Id, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>The theme with that id, or the default when nothing has that id.</summary>
    /// <remarks>
    /// A settings file naming a theme this build does not have resolves to the default rather than to
    /// an error: it is how a downgrade reads a newer file, and a missing theme is not worth refusing to
    /// start over.
    /// </remarks>
    public static ThemeDefinition Get(string? id)
        => id is not null && ById.TryGetValue(id, out ThemeDefinition? theme) ? theme : ById[DefaultId];
}
