using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TestFramework.DebugUI.Docking;
using TestFramework.DebugUI.Layout;
using TestFramework.DebugUI.State;
using TestFramework.DebugUI.Theme;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// The family's rules, checked against what this repository compiles rather than trusted to have been
/// followed.
/// </summary>
/// <remarks>
/// Every assembly the application ships is reachable from this suite, so this is where they are
/// checked. The launcher ships separately and keeps the same rules in its own suite.
/// </remarks>
public class ConventionTests
{
    /// <summary>
    /// Each assembly the application ships, with the one suite it may open its internals to.
    /// </summary>
    /// <remarks>
    /// Named rather than derived: the application's suite is <c>App.Tests</c>, not
    /// <c>TestFramework.DebugUI.Tests</c>, so "the assembly's name plus <c>.Tests</c>" would refuse the one
    /// grant this repository means to make.
    /// </remarks>
    private static readonly (Assembly Assembly, string OwnSuite)[] Shipped =
    [
        (typeof(ThemeApplier).Assembly, "TestFramework.DebugUI.App.Tests"),
        (typeof(MainState).Assembly, "TestFramework.DebugUI.State.Tests"),
        (typeof(DockLayout).Assembly, "TestFramework.DebugUI.Docking.Tests"),
        (typeof(LayoutOptions).Assembly, "TestFramework.DebugUI.Layout.Tests"),
    ];

    [Fact]
    public void NothingHereSerialisesWithSystemTextJson()
    {
        // The family picked Newtonsoft.Json. Two libraries mean two sets of attributes, two notions of what
        // null means, and values that survive one round trip but not the other. Checked against the compiled
        // assemblies, because a stray using is invisible in a diff.
        string[] offenders =
        [
            .. Shipped.Select(shipped => shipped.Assembly)
                .Append(typeof(ConventionTests).Assembly)
                .Where(assembly => assembly.GetReferencedAssemblies()
                    .Any(reference => string.Equals(reference.Name, "System.Text.Json", StringComparison.Ordinal)))
                .Select(assembly => assembly.GetName().Name ?? assembly.FullName ?? string.Empty)
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryInternalsVisibleToGrantNamesTheAssemblysOwnSuite()
    {
        // A grant to anything else is a private handshake: it lets one assembly reach what no other can, and
        // hides the surface that is missing. The attribute value may carry a public key, which is not part of
        // who is being trusted.
        string[] strays =
        [
            .. Shipped.SelectMany(shipped => shipped.Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(grant => grant.AssemblyName.Split(',')[0].Trim())
                .Where(target => !string.Equals(target, shipped.OwnSuite, StringComparison.Ordinal))
                .Select(target => $"{shipped.Assembly.GetName().Name} opens its internals to {target}"))
        ];

        Assert.Empty(strays);
    }
}
