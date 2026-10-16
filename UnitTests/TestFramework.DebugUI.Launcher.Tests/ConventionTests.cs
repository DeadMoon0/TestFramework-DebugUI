using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TestFramework.DebugUI.Launcher.Tests;

/// <summary>
/// The family's rules, checked against the launcher as it is compiled rather than trusted to have been
/// followed.
/// </summary>
/// <remarks>
/// The launcher ships on its own, so it is checked here rather than beside the application's assemblies.
/// </remarks>
public class ConventionTests
{
    private static readonly Assembly Launcher = typeof(LauncherPaths).Assembly;

    [Fact]
    public void TheLauncherSerialisesWithNewtonsoftOnly()
    {
        // The family picked Newtonsoft.Json. Checked against the compiled assemblies, because a stray using
        // is invisible in a diff.
        string[] offenders =
        [
            .. new[] { Launcher, typeof(ConventionTests).Assembly }
                .Where(assembly => assembly.GetReferencedAssemblies()
                    .Any(reference => string.Equals(reference.Name, "System.Text.Json", StringComparison.Ordinal)))
                .Select(assembly => assembly.GetName().Name ?? assembly.FullName ?? string.Empty)
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheLauncherOpensItsInternalsToItsOwnSuiteOnly()
    {
        // A grant to anything else lets one assembly reach what no other can, and hides the surface that is
        // missing. The attribute value may carry a public key, which is not part of who is being trusted.
        string[] strays =
        [
            .. Launcher.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(grant => grant.AssemblyName.Split(',')[0].Trim())
                .Where(target => !string.Equals(target, typeof(ConventionTests).Assembly.GetName().Name, StringComparison.Ordinal))
        ];

        Assert.Empty(strays);
    }
}
