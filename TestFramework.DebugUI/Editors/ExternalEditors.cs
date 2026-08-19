using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TestFramework.DebugUI.Editors;

/// <summary>
/// An editor installed on this machine.
/// </summary>
/// <remarks>
/// The icon is read out of the executable rather than shipped. Microsoft's marks are theirs, and a button
/// that claims to open Visual Studio has to show Visual Studio's own icon or it is guessing — so the tool
/// carries none and takes each one from the program it found.
/// </remarks>
public sealed record ExternalEditor
{
    /// <summary>Gets the editor's name, for the tooltip.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the executable to launch.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Gets a value indicating whether this editor opens a folder rather than a solution file.</summary>
    public required bool WantsFolder { get; init; }

    /// <summary>Gets the editor's own icon, taken from its executable, or null if it had none to give.</summary>
    public ImageSource? Icon { get; init; }
}

/// <summary>Which editors were found.</summary>
public sealed record EditorSet
{
    /// <summary>Nothing installed, which is what a machine reports until it has been looked at.</summary>
    public static EditorSet None { get; } = new();

    /// <summary>Gets Visual Studio, when it is installed.</summary>
    public ExternalEditor? VisualStudio { get; init; }

    /// <summary>Gets VS Code, when it is installed.</summary>
    public ExternalEditor? Code { get; init; }
}

/// <summary>
/// Finds the editors on this machine, once.
/// </summary>
/// <remarks>
/// Looked for in the background and remembered. Finding Visual Studio means running vswhere, which is a
/// process launch — doing that on the way to showing the window would delay the window for something no
/// reader has asked for yet, and doing it per click would repeat it forever.
/// </remarks>
public static class ExternalEditors
{
    private static Task<EditorSet>? resolution;

    /// <summary>The editors on this machine, looked for on the first call and remembered after.</summary>
    public static Task<EditorSet> ResolveAsync() => resolution ??= Task.Run(Resolve);

    /// <summary>
    /// Opens a target in an editor.
    /// </summary>
    /// <remarks>
    /// Reports rather than throws. An editor that has been uninstalled since it was found, or that refuses
    /// to start, is a disappointment and not a reason to take the debugger down with it.
    /// </remarks>
    public static bool TryOpen(ExternalEditor editor, IReadOnlyList<string> arguments)
    {
        if (editor is null || arguments is null || arguments.Count == 0)
            return false;

        try
        {
            ProcessStartInfo start = new(editor.ExecutablePath) { UseShellExecute = false };

            // Passed as arguments rather than interpolated into a command line, so a path with a space in
            // it does not arrive as two arguments — which is every path under Program Files.
            foreach (string argument in arguments)
                start.ArgumentList.Add(argument);

            using Process? started = Process.Start(start);

            return started is not null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine(e);
            return false;
        }
    }

    /// <summary>The solution files in a directory, or none when it cannot be read.</summary>
    internal static IEnumerable<string> SolutionsIn(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.sln");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
            return [];
        }
    }

    private static EditorSet Resolve() => new()
    {
        Code = FindCode(),
        VisualStudio = FindVisualStudio()
    };

    private static ExternalEditor? FindCode()
    {
        string? path = EditorPaths.FirstExisting(
            EditorPaths.CodeCandidates(Environment.GetEnvironmentVariable),
            File.Exists);

        return path is null ? null : new ExternalEditor
        {
            Name = "VS Code",
            ExecutablePath = path,
            Icon = EditorIcon.From(path),

            // Handed a file, VS Code opens that one file with no project around it. The folder is what
            // makes it an editor you can work in.
            WantsFolder = true
        };
    }

    /// <summary>
    /// Asks vswhere where Visual Studio is.
    /// </summary>
    /// <remarks>
    /// vswhere rather than a guessed path: Visual Studio can be installed side by side, in any edition,
    /// under a directory the user chose, and its own installer ships this tool precisely so that nobody
    /// has to guess. If the tool is missing, Visual Studio is not installed.
    /// </remarks>
    private static ExternalEditor? FindVisualStudio()
    {
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)")
                               ?? Environment.GetEnvironmentVariable("ProgramFiles");

        if (programFiles is null)
            return null;

        string vswhere = Path.Combine(programFiles, EditorPaths.VsWhereRelativePath);

        if (!File.Exists(vswhere))
            return null;

        string? productPath = EditorPaths.ProductPathFrom(Ask(vswhere, EditorPaths.VsWhereArguments));

        return productPath is null || !File.Exists(productPath) ? null : new ExternalEditor
        {
            Name = "Visual Studio",
            ExecutablePath = productPath,
            Icon = EditorIcon.From(productPath),
            WantsFolder = false
        };
    }

    /// <summary>
    /// Runs a tool and returns what it printed, or null if it did not manage to print anything.
    /// </summary>
    /// <remarks>
    /// Bounded by a timeout. This runs while the window is being set up, and a tool that hangs would
    /// otherwise leave a button that never resolves either way.
    /// </remarks>
    private static string? Ask(string executable, string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            string output = process.StandardOutput.ReadToEnd();

            return process.WaitForExit(TimeSpan.FromSeconds(10)) ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine(e);
            return null;
        }
    }
}
