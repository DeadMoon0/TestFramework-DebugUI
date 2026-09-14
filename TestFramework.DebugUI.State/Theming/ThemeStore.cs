using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <remarks>
    /// See <see cref="ToolPaths"/> for why this is not under <c>AppData</c>. A theme is written by
    /// hand, in a folder someone opened themselves, so it has to be the folder the tool reads.
    /// </remarks>
    public static string DefaultDirectory => Path.Combine(ToolPaths.ToolFolder, ThemesFolderName);

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
                File.WriteAllText(path, Example.ReplaceLineEndings());
                // ^ The literal's line endings come from however this source file was checked out,
                //   and the breaks inside a wrapped list are written as \n. Normalising once here is
                //   what stops the example arriving with two kinds of line ending in it.

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

    /// <summary>Every id a theme file may inherit from, as the example lists them.</summary>
    private static string InheritableIds =>
        Wrapped(BuiltInThemes.All.Select(theme => theme.Id), "Inherit from any of: ".Length);

    /// <summary>Every backdrop a theme file may name, as the example lists them.</summary>
    private static string BackdropNames =>
        Wrapped(Enum.GetNames<BackdropRecipe>(), "Backdrops: ".Length);

    /// <summary>
    /// Lays a generated list out across comment lines, the way a hand-typed one was laid out.
    /// </summary>
    /// <remarks>
    /// The lists these replace were wrapped, and the file is one somebody opens in an editor to change
    /// a colour — so a single line of every theme's name, growing with each one added, would be worse
    /// than the staleness that generating them fixed.
    /// </remarks>
    /// <param name="names">The list, in the order it should read.</param>
    /// <param name="firstLineUsed">
    /// How much of the first line the label before it has already taken. Without it the first line
    /// runs over by exactly the length of the words introducing it, which is the line most likely to
    /// be too long and the one nothing else would account for.
    /// </param>
    private static string Wrapped(IEnumerable<string> names, int firstLineUsed)
    {
        const int Width = 74;

        StringBuilder all = new();
        int lineLength = firstLineUsed;

        foreach (string name in names)
        {
            if (all.Length > 0)
            {
                all.Append(',');

                if (lineLength + name.Length > Width)
                {
                    all.Append(ExampleCommentBreak);
                    lineLength = 0;
                }
                else
                {
                    all.Append(' ');
                    lineLength++;
                }
            }

            all.Append(name);
            lineLength += name.Length + 1;
        }

        return all.ToString();
    }

    /// <summary>What a wrapped list starts its next line with, indentation and comment marker included.</summary>
    /// <remarks>
    /// Two spaces, which is where the example's lines sit once the raw string literal's own
    /// indentation has been stripped — not where they sit in this file. The written file is what has
    /// to line up, and <see cref="WriteExample"/> settles the line ending for all of it.
    /// </remarks>
    private const string ExampleCommentBreak = "\n  // ";

    /// <summary>
    /// The example file, comments and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comments are not a mistake and must not be "fixed": the reader this store uses accepts them,
    /// and they are the only documentation of the format that arrives in the same place as the thing
    /// being documented.
    /// </para>
    /// <para>
    /// The two lists in it are read off the things they list rather than typed out, because a list
    /// typed out here is a list that goes stale — which is exactly what happened to both of them when
    /// the Origin pair was added, and nothing failed to say so.
    /// </para>
    /// </remarks>
    private static readonly string Example = $$"""
        {
          // A theme is a built-in with some things changed. Everything you leave out stays
          // as "inherits" has it, including colours added by a later version of the tool.
          //
          // Inherit from any of: {{InheritableIds}}.
          //
          // The file name is the theme's id, so this one is "my-theme".

          "name": "My theme",
          "inherits": "slate-dark",

          "colours": {
            "Accent": "#FFE07A3F",
            "StateTimeout": "#FFE8C547"
          },

          // Backdrops: {{BackdropNames}}.
          // Clear paints nothing at all, and lets the desktop show through WindowTint.
          "backdrop": {
            "recipe": "Orbits",
            "blur": 14
          }
        }
        """;
}
