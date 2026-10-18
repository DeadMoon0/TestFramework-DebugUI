using System;
using System.IO;
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

    /// <summary>
    /// The projects whose views and services are handed the store rather than reaching for one.
    /// </summary>
    private static readonly string[] HandedTheStore = ["TestFramework.DebugUI", "TestFramework.DebugUI.State"];

    /// <summary>What reaching for, or publishing, a store for the whole process looks like in source.</summary>
    private static readonly string[] ProcessWideStore = ["StateStore<MainState>.Default", ".MakeDefault(", "BuildAndMakeDefault"];

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

    [Fact]
    public void NothingRaisesAProcessWideEvent()
    {
        // A static event is a store for the whole process with a different name: every window, and every test,
        // that subscribes shares it, and a subscriber nobody removes keeps its window alive. What changes is an
        // instance, handed to whatever watches it.
        string[] offenders =
        [
            .. Shipped.SelectMany(shipped => shipped.Assembly.GetTypes())
                .SelectMany(type => type.GetEvents(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Select(raised => $"{type.FullName}.{raised.Name}"))
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoViewReachesForAProcessWideStore()
    {
        // A view is handed the store it shows. One that reached for a process-wide default instead would
        // work only once something had published one, and a test could only build it by publishing one for
        // every test at once. Read from the source rather than the assemblies, because a static property
        // read leaves nothing in the metadata a reflection check could find.
        string root = RepositoryRoot();

        string[] sources =
        [
            .. HandedTheStore
                .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
                .Where(path => !IsBuildOutput(root, path))
        ];

        // Guarded, because a scan that found nothing to read would pass for the wrong reason.
        Assert.NotEmpty(sources);

        string[] offenders =
        [
            .. sources
                .Where(path => ProcessWideStore.Any(pattern => File.ReadAllText(path).Contains(pattern, StringComparison.Ordinal)))
                .Select(path => Path.GetRelativePath(root, path))
        ];

        Assert.Empty(offenders);
    }

    /// <summary>Whether a source file is something the build wrote rather than something somebody did.</summary>
    private static bool IsBuildOutput(string root, string path)
        => Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "obj" or "bin");

    /// <summary>
    /// The repository this suite was compiled from, found from this file's own path.
    /// </summary>
    /// <remarks>
    /// The compiler writes the path in, so it is the checkout that was built rather than wherever the test
    /// happens to run from.
    /// </remarks>
    private static string RepositoryRoot([CallerFilePath] string here = "")
    {
        for (DirectoryInfo? directory = new FileInfo(here).Directory; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TestFramework.DebugUI.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No TestFramework.DebugUI.slnx above {here}.");
    }
}
