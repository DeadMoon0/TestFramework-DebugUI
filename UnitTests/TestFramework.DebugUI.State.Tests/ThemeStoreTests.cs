using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using TestFramework.DebugUI.State.Theming;
using Xunit;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// That the themes folder can be anything at all and the tool still opens.
/// </summary>
/// <remarks>
/// A theme is a convenience, and the store's whole contract is that no state of that folder — missing,
/// empty, full of files that are not JSON, full of themes that name a base nobody has — is worth a tool
/// that will not start.
/// </remarks>
public class ThemeStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "tf-themes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AMissingFolderGivesTheBuiltInsAndSaysNothing()
    {
        List<string> reported = [];

        ImmutableArray<ThemeDefinition> themes = new ThemeStore(folder, reported.Add).Load();

        Assert.Equal(BuiltInThemes.All.Length, themes.Length);
        Assert.Empty(reported);
    }

    [Fact]
    public void AThemeInTheFolderIsOfferedAlongsideTheBuiltIns()
    {
        Write("mine.json", """
            { "name": "Mine", "inherits": "slate-dark", "colours": { "Accent": "#FF00FF00" } }
            """);

        ImmutableArray<ThemeDefinition> themes = new ThemeStore(folder).Load();

        ThemeDefinition mine = Assert.Single(themes.Where(theme => theme.IsCustom));

        Assert.Equal("mine", mine.Id);
        Assert.Equal("Mine", mine.Name);
        Assert.Equal(new ThemeColour(0xFF00FF00u), mine.Colour(nameof(ThemePalette.Accent)));
    }

    /// <summary>
    /// That comments in a theme file are read rather than rejected.
    /// </summary>
    /// <remarks>
    /// The example the tool writes is mostly comments, because a file that explains itself is better
    /// documentation than a page somewhere else. If the reader ever stopped accepting them, that example
    /// would be the first thing to break.
    /// </remarks>
    [Fact]
    public void TheExampleTheToolWritesIsAThemeItCanRead()
    {
        ThemeStore store = new(folder);

        string? path = store.WriteExample();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        ThemeDefinition example = Assert.Single(store.Load().Where(theme => theme.IsCustom));

        Assert.Equal("my-theme", example.Id);
        Assert.Equal(BackdropRecipe.Orbits, example.Backdrop.Recipe);
    }

    /// <summary>That the example is never written over the file somebody has been editing.</summary>
    [Fact]
    public void TheExampleIsNotWrittenTwice()
    {
        ThemeStore store = new(folder);

        store.WriteExample();
        File.WriteAllText(Path.Combine(folder, "my-theme.json"), """{ "inherits": "tide-dark" }""");

        store.WriteExample();

        Assert.Equal(BackdropRecipe.Scatter, Assert.Single(store.Load().Where(t => t.IsCustom)).Backdrop.Recipe);
    }

    [Fact]
    public void AFileThatIsNotJsonIsReportedAndTheRestStillLoad()
    {
        Write("broken.json", "this is not json {");
        Write("good.json", """{ "inherits": "ember-dark" }""");

        List<string> reported = [];
        ImmutableArray<ThemeDefinition> themes = new ThemeStore(folder, reported.Add).Load();

        Assert.Contains(reported, problem => problem.Contains("broken"));
        Assert.Single(themes.Where(theme => theme.IsCustom));
    }

    /// <summary>
    /// That a file cannot take a built-in theme away.
    /// </summary>
    /// <remarks>
    /// Shadowing is the one behaviour that could leave somebody stuck: a broken <c>slate-dark.json</c>
    /// would replace the theme they would otherwise have gone back to, and the picker would offer no way
    /// out of it.
    /// </remarks>
    [Fact]
    public void AFileNamedAfterABuiltInIsSkipped()
    {
        Write("slate-dark.json", """{ "inherits": "contrast-light", "colours": { "Accent": "#FFFF0000" } }""");

        List<string> reported = [];
        ImmutableArray<ThemeDefinition> themes = new ThemeStore(folder, reported.Add).Load();

        Assert.Contains(reported, problem => problem.Contains("slate-dark"));
        Assert.Equal(BuiltInThemes.All.Length, themes.Length);
        Assert.Equal(
            BuiltInThemes.SlateDark.Colour(nameof(ThemePalette.Accent)),
            themes.Single(theme => theme.Id == "slate-dark").Colour(nameof(ThemePalette.Accent)));
    }

    [Fact]
    public void AThemeNamingAMissingBaseIsReportedAndNotOffered()
    {
        Write("lost.json", """{ "inherits": "slate-mauve" }""");

        List<string> reported = [];
        ImmutableArray<ThemeDefinition> themes = new ThemeStore(folder, reported.Add).Load();

        Assert.Contains(reported, problem => problem.Contains("slate-mauve"));
        Assert.Empty(themes.Where(theme => theme.IsCustom));
    }

    private void Write(string name, string content)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), content);
    }
}
