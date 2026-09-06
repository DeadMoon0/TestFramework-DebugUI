using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TestFramework.DebugUI.State.Diagnostics;

namespace TestFramework.DebugUI.State.Theming;

/// <summary>
/// The themes available to the tool: the ten compiled in, plus whatever is in the themes folder.
/// </summary>
/// <remarks>
/// <para>
/// Beside the settings file rather than in the installed folder, for the same reason the settings are:
/// the launcher keeps several versions side by side and replaces them wholesale, and a theme somebody
/// wrote should not be deleted by an update.
/// </para>
/// <para>
/// <b>Nothing here throws at the caller.</b> A theme is a preference. An unreadable folder, a file that
/// is not JSON, a theme naming a base that does not exist — each of those resolves to a reported line
/// and the ten built-ins, because none of them is worth a tool that will not start.
/// </para>
/// </remarks>
public sealed class ThemeStore
{
    private const string FolderName = "TestFramework";
    private const string ToolFolderName = "DebugUI";
    private const string ThemesFolderName = "themes";

    private readonly string directory;
    private readonly Action<string>? report;

    /// <summary>Creates a store over the default location.</summary>
    /// <param name="report">Told about a file that could not be used, so it can reach the message feed.</param>
    public ThemeStore(Action<string>? report = null)
        : this(DefaultDirectory, report)
    {
    }

    /// <summary>Creates a store over an explicit folder, which is what the tests use.</summary>
    public ThemeStore(string directory, Action<string>? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        this.directory = directory;
        this.report = report;
    }

    /// <summary>Where custom themes live unless told otherwise.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        ToolFolderName,
        ThemesFolderName);

    /// <summary>The folder this store reads.</summary>
    public string DirectoryPath => directory;

    /// <summary>
    /// Every theme, built-in first.
    /// </summary>
    /// <remarks>
    /// A custom theme whose id collides with a built-in is skipped rather than allowed to shadow it.
    /// Letting a file replace <c>slate-dark</c> means somebody's broken theme can take away the one
    /// they would have gone back to.
    /// </remarks>
    public ImmutableArray<ThemeDefinition> Load()
    {
        List<ThemeDefinition> themes = [.. BuiltInThemes.All];

        foreach ((string id, ThemeFile? file) in Read())
        {
            if (BuiltInThemes.ById.ContainsKey(id))
            {
                report?.Invoke($"The theme '{id}' was skipped: a built-in theme already has that name.");
                continue;
            }

            ThemeResolution resolved = ThemeResolver.Resolve(id, file, BuiltInThemes.ById);

            foreach (string problem in resolved.Problems)
                report?.Invoke(problem);

            if (resolved.Theme is not null)
                themes.Add(resolved.Theme);
        }

        return [.. themes];
    }

    /// <summary>
    /// Writes an example theme into the folder, and returns where it went.
    /// </summary>
    /// <remarks>
    /// The settings panel offers this rather than a page of documentation. A file that already exists,
    /// already inherits something real and already has the right key spellings in it is a better
    /// explanation of the format than any prose, and it means the folder the panel opens is never
    /// empty.
    /// </remarks>
    public string? WriteExample()
    {
        string path = Path.Combine(directory, "my-theme.json");

        try
        {
            Directory.CreateDirectory(directory);

            if (!File.Exists(path))
                File.WriteAllText(path, Example);

            return path;
        }
        catch (Exception e)
        {
            Log.Write(e);
            report?.Invoke($"The example theme could not be written. {e.Message}");

            return null;
        }
    }

    /// <summary>
    /// Every <c>*.json</c> in the folder, paired with its id.
    /// </summary>
    /// <remarks>
    /// A null file means the entry could not be read or was not JSON at all; the resolver turns that
    /// into the message. Kept apart from resolution so that everything below this line is the only
    /// part that touches a disk.
    /// </remarks>
    private IEnumerable<(string Id, ThemeFile? File)> Read()
    {
        string[] paths;

        try
        {
            if (!Directory.Exists(directory))
                yield break;

            paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            Log.Write(e);
            report?.Invoke($"The themes folder could not be read, so only the built-in themes are available. {e.Message}");

            yield break;
        }

        foreach (string path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            yield return (Path.GetFileNameWithoutExtension(path), ReadOne(path));
    }

    private ThemeFile? ReadOne(string path)
    {
        try
        {
            return JsonConvert.DeserializeObject<ThemeFile>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Log.Write(e);

            return null;
        }
    }

    /// <summary>
    /// The example file, comments and all.
    /// </summary>
    /// <remarks>
    /// The comments are not a mistake and must not be "fixed": the reader this store uses accepts them,
    /// and they are the only documentation of the format that arrives in the same place as the thing
    /// being documented.
    /// </remarks>
    private const string Example = """
        {
          // A theme is a built-in with some things changed. Everything you leave out stays
          // as "inherits" has it, including colours added by a later version of the tool.
          //
          // Inherit from any of: slate-dark, slate-light, ember-dark, ember-light,
          // tide-dark, tide-light, glass-dark, glass-light, contrast-dark, contrast-light.
          //
          // The file name is the theme's id, so this one is "my-theme".

          "name": "My theme",
          "inherits": "slate-dark",

          "colours": {
            "Accent": "#FFE07A3F",
            "StateTimeout": "#FFE8C547"
          },

          // Backdrops: Clear, Flat, Hexfield, Orbits, Scatter, Lattice, Arcs, Ridges, Dunes.
          // Clear paints nothing at all, and lets the desktop show through WindowTint.
          "backdrop": {
            "recipe": "Orbits",
            "blur": 14
          }
        }
        """;
}
