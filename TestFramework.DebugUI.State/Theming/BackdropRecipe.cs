namespace TestFramework.DebugUI.State.Theming;

/// <summary>
/// The shapes the window knows how to paint behind itself.
/// </summary>
/// <remarks>
/// <para>
/// A closed set rather than a path or a file name. A theme is data somebody can write by hand, and the
/// one thing a hand-written theme must not be able to do is hand the tool a drawing to execute; naming
/// a recipe lets a theme choose its geometry while the drawing stays compiled in.
/// </para>
/// <para>
/// Every one of them is drawn from the same four palette entries — base, near, far and one glow — so a
/// recipe swapped onto another theme comes out in that theme's colours rather than looking borrowed.
/// </para>
/// </remarks>
public enum BackdropRecipe
{
    /// <summary>
    /// Nothing is painted. What shows through is the acrylic blur behind the window, tinted by
    /// <c>WindowTint</c>, and whatever the user has on their desktop.
    /// </summary>
    Clear,

    /// <summary>An even wash of <c>BackdropBase</c>, with no geometry and no glow.</summary>
    Flat,

    /// <summary>A honeycomb with cells missing and a few lit.</summary>
    Hexfield,

    /// <summary>Rings around a centre just off the frame, with loose discs below.</summary>
    Orbits,

    /// <summary>Discs sorted back to front, some drawn as outlines.</summary>
    Scatter,

    /// <summary>An isometric grid with lit nodes — the board's own shape, behind the board.</summary>
    Lattice,

    /// <summary>Nested sweeps out of one corner.</summary>
    Arcs,

    /// <summary>Layered ridges. The shape the tool shipped with.</summary>
    Ridges,

    /// <summary>Fewer, rounder bands and a low sun.</summary>
    Dunes,

    /// <summary>
    /// The tool's original ridges, copied rather than described.
    /// </summary>
    /// <remarks>
    /// The one recipe that is not a rule for drawing something. <see cref="Ridges"/> is the family the
    /// original belonged to, redrawn to the painter's own proportions and shaded from the theme; this is
    /// the twelve authored paths themselves, in the square they were authored in. It exists because a
    /// theme that means "the way it used to look" cannot be an approximation of it.
    /// </remarks>
    Origin
}
