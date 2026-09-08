using System;
using System.IO;
using TestFramework.DebugUI.State.Diagnostics;
using TestFramework.DebugUI.State.Settings;
using TestFramework.DebugUI.State.Theming;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers where the tool keeps a user's own files.
/// </summary>
/// <remarks>
/// Two things worth holding, and neither is the literal path. The first is that everything lands in
/// one folder, because it used to land in three that happened to agree. The second is that the folder
/// is not under <c>AppData</c> — which looks like an arbitrary preference and is not: a packaged
/// application's writes there are virtualized into a per-package store, so a log would not be where
/// its reader was told to look and a hand-written theme would be read from a folder the tool never
/// writes. Anyone tidying this back to <c>LocalApplicationData</c> would reintroduce that silently,
/// so it fails here instead.
/// </remarks>
public sealed class ToolPathsTests
{
    [Fact]
    public void TheToolsFilesAreNotKeptUnderAppData()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.DoesNotContain(appData, ToolPaths.Root, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(roaming, ToolPaths.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRootSitsInTheUsersProfile()
    {
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ToolPaths.Root,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EverythingTheToolWritesSharesOneFolder()
    {
        // The point of the type. Three stores used to spell the same two folder names out
        // separately, so the root could only be moved by finding all three.
        Assert.Equal(ToolPaths.ToolFolder, Path.GetDirectoryName(SettingsStore.DefaultPath));
        Assert.Equal(ToolPaths.ToolFolder, Path.GetDirectoryName(Log.DefaultPath));
        Assert.Equal(ToolPaths.ToolFolder, Path.GetDirectoryName(ThemeStore.DefaultDirectory));
    }
}
