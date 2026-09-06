using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>
/// Every colour a theme has to name, once.
/// </summary>
/// <remarks>
/// <para>
/// Every member is <c>required</c>, and that is the point. "A theme defines every key" is the rule a
/// theme system usually keeps in a test, or in a reviewer's head; here the compiler keeps it. Adding a
/// colour below breaks every palette in the file until each one says what it should be, which is the
/// moment somebody is actually thinking about what it should be.
/// </para>
/// <para>
/// A property name is the resource key. There is no table mapping one to the other, because a table is
/// a second place the two spellings could disagree — <see cref="Keys"/> and <see cref="ToLookup"/> read
/// the properties themselves.
/// </para>
/// <para>
/// This is the authoring shape, used by <see cref="BuiltInThemes"/>. What actually gets painted is the
/// lookup it produces: a theme file can override entries in that, and overriding can only replace a
/// colour, never remove one, so a resolved palette is complete for the same reason this one is.
/// </para>
/// </remarks>
public sealed record ThemePalette
{
    // Surfaces: what things sit on, from the window's own back to a card lifted off it.
    public required ThemeColour SurfaceSunken { get; init; }

    public required ThemeColour SurfacePanel { get; init; }

    public required ThemeColour SurfaceRaised { get; init; }

    public required ThemeColour SurfaceRaisedHover { get; init; }

    public required ThemeColour SurfaceCard { get; init; }

    public required ThemeColour SurfaceDivider { get; init; }

    public required ThemeColour SurfaceOverlay { get; init; }

    public required ThemeColour ConnectorStrip { get; init; }

    // Edges and depth: the strokes that separate one dark surface from the dark surface behind it.
    public required ThemeColour PanelEdge { get; init; }

    public required ThemeColour IconGroupEdge { get; init; }

    public required ThemeColour Scrim { get; init; }

    public required ThemeColour PipeShadow { get; init; }

    // Text, in the three weights everything is written in.
    public required ThemeColour TextPrimary { get; init; }

    public required ThemeColour TextSecondary { get; init; }

    public required ThemeColour TextFaint { get; init; }

    // Lifecycle: the colours a step, a run and a log line all report their state in.
    public required ThemeColour StateNotRun { get; init; }

    public required ThemeColour StateRunning { get; init; }

    public required ThemeColour StateComplete { get; init; }

    public required ThemeColour StateError { get; init; }

    public required ThemeColour StateTimeout { get; init; }

    public required ThemeColour StateSkipped { get; init; }

    public required ThemeColour StatePaused { get; init; }

    /// <summary>The one colour that means "this is what you are looking at".</summary>
    public required ThemeColour Accent { get; init; }

    /// <summary>
    /// What is drawn on top of the accent.
    /// </summary>
    /// <remarks>
    /// Not <see cref="TextPrimary"/>, which is what reads on a <em>surface</em>. A switch's knob sits on
    /// the accent itself, and the accent is a saturated mid-tone that goes light in some themes and dark
    /// in others — so the ink on it is a decision each theme makes rather than one that can be derived
    /// from the page. Getting this wrong is a black knob on a blue switch, which is what it was.
    /// </remarks>
    public required ThemeColour AccentInk { get; init; }

    // What flows between steps, and the wash the port sits in.
    public required ThemeColour FlowVariable { get; init; }

    public required ThemeColour FlowVariableSurface { get; init; }

    public required ThemeColour FlowArtifact { get; init; }

    public required ThemeColour FlowArtifactSurface { get; init; }

    // A comparison, line by line.
    public required ThemeColour DiffAddedSurface { get; init; }

    public required ThemeColour DiffAddedText { get; init; }

    public required ThemeColour DiffRemovedSurface { get; init; }

    public required ThemeColour DiffRemovedText { get; init; }

    public required ThemeColour DiffContextText { get; init; }

    // What a reader draws in, and what their marks are lifted off the board by.

    /// <summary>
    /// The neutral marker, which is not white in every theme.
    /// </summary>
    /// <remarks>
    /// Called <c>InkWhite</c> until there was a light theme to be white against. The name described the
    /// colour rather than the job, and the job is "the mark you make when you do not mean a colour".
    /// </remarks>
    public required ThemeColour InkNeutral { get; init; }

    public required ThemeColour InkCyan { get; init; }

    public required ThemeColour InkMagenta { get; init; }

    public required ThemeColour InkViolet { get; init; }

    public required ThemeColour InkOrange { get; init; }

    public required ThemeColour InkHalo { get; init; }

    // What kind of thing a value is. Not lifecycle colours and not the accent: these say "a row", "a
    // document", "a blob" - facts about the value rather than anything that happened to it.
    //
    // The same across every theme of a mode rather than retuned per family. A value's kind does not
    // change with the window's temperature; what changes is only the lightness the hue has to read at,
    // and that is what the mode decides.

    /// <summary>A row out of a relational table.</summary>
    public required ThemeColour ValueRelational { get; init; }

    /// <summary>A document with a lifecycle of its own.</summary>
    public required ThemeColour ValueDocument { get; init; }

    /// <summary>Bytes: a blob, a file's contents, anything not meant to be read as text.</summary>
    public required ThemeColour ValueBinary { get; init; }

    /// <summary>An entity in a table store.</summary>
    public required ThemeColour ValueTabular { get; init; }

    /// <summary>A plain value: a number, a string, a list, an object a step assigned.</summary>
    public required ThemeColour ValuePlain { get; init; }

    /// <summary>
    /// The tint of the acrylic blur Windows draws behind the whole window.
    /// </summary>
    /// <remarks>
    /// The window has asked for that blur since it was written, with this colour written into the call
    /// as <c>Color.FromArgb(200, 0, 0, 0)</c> — so every surface in the tool has been sitting behind a
    /// seventy-eight percent black wash that no theme could see, and a light theme drawn without
    /// knowing that would have come out grey. Naming it is what makes a see-through theme possible at
    /// all: drop the alpha and the desktop is the background.
    /// </remarks>
    public required ThemeColour WindowTint { get; init; }

    // The backdrop the window paints for itself, behind everything and in front of the blur.

    /// <summary>
    /// What the backdrop is painted on.
    /// </summary>
    /// <remarks>
    /// Transparent in a see-through theme, which is exactly how such a theme is expressed. It is still
    /// required there, because acrylic is off under Remote Desktop, on battery saver, and whenever the
    /// user turns transparency effects off — and this is what is left when it is.
    /// </remarks>
    public required ThemeColour BackdropBase { get; init; }

    /// <summary>The near end of the backdrop's ramp: the shapes closest to the reader.</summary>
    public required ThemeColour BackdropNear { get; init; }

    /// <summary>The far end of the same ramp.</summary>
    public required ThemeColour BackdropFar { get; init; }

    /// <summary>The one light source in the backdrop. Alpha governs how strongly it reads.</summary>
    public required ThemeColour BackdropGlow { get; init; }

    /// <summary>
    /// Declared first, because the two members below are initialised from it and static initialisers
    /// run in the order they are written.
    /// </summary>
    private static ImmutableArray<PropertyInfo> Readers { get; } = typeof(ThemePalette)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(ThemeColour))
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToImmutableArray();

    /// <summary>
    /// The name of every colour a theme has to give.
    /// </summary>
    /// <remarks>
    /// Read off the type rather than listed, so a colour added above is covered without anyone
    /// remembering a second place.
    /// </remarks>
    public static ImmutableArray<string> Keys { get; } = Readers.Select(reader => reader.Name).ToImmutableArray();

    /// <summary>
    /// The palette as the applier and the theme files see it: colours by key.
    /// </summary>
    public ImmutableDictionary<string, ThemeColour> ToLookup()
        => Readers.ToImmutableDictionary(
            reader => reader.Name,
            reader => (ThemeColour)reader.GetValue(this)!,
            StringComparer.Ordinal);
}
