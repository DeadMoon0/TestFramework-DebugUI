using System;
using System.Collections.Generic;
using System.Windows;

namespace TestFramework.DebugUI.Theme;

/// <summary>
/// Proof that a dictionary's brushes have been made movable, and the dictionary itself.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="ThemeApplier.Prepare"/> makes one. Everything that needs the brushes to be movable
/// before it runs — the window, whose markup resolves them while it is built, and anything that applies
/// a theme — asks for this instead of for the dictionary, so calling it before the palette was prepared
/// does not compile rather than producing a window that changes theme everywhere except in one panel.
/// </para>
/// <para>
/// The other half of the ordering, that no window was built <em>before</em> the preparation, is a fact
/// about the running application rather than about the code, and <see cref="ThemeApplier.Prepare"/>
/// checks it there.
/// </para>
/// </remarks>
internal sealed class PreparedTheme
{
    /// <summary>Wraps a dictionary <see cref="ThemeApplier.Prepare"/> has just prepared.</summary>
    internal PreparedTheme(ResourceDictionary resources, IReadOnlyList<string> missing)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(missing);

        Resources = resources;
        Missing = missing;
    }

    /// <summary>The dictionary holding the prepared brushes.</summary>
    public ResourceDictionary Resources { get; }

    /// <summary>The colours the markup has no brush for, which is a defect rather than a bad theme.</summary>
    public IReadOnlyList<string> Missing { get; }
}
