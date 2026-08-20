using System;
using System.Collections.Immutable;
using System.IO;
using TestFramework.DebugUI.State;

using TestFramework.DebugUI.Docking;

namespace TestFramework.DebugUI.State.Tests;

/// <summary>
/// Covers reading and writing what the tool remembers.
/// </summary>
/// <remarks>
/// Almost all of this is about failing safely. Settings are a convenience, and the way a settings file
/// ruins a tool is never by being absent — it is by being unreadable and taking the whole start-up with
/// it, or by being half-written and silently discarding everything the user had set.
/// </remarks>
public class SettingsStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "tf-settings-" + Guid.NewGuid().ToString("N"));

    private string File0 => Path.Combine(folder, "settings.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Fact]
    public void NothingSavedYetReadsAsDefaults()
    {
        UiSettings settings = new SettingsStore(File0).Load();

        Assert.Equal(UiSettings.CurrentVersion, settings.Version);
        Assert.Null(settings.Window);
        Assert.Empty(settings.Breakpoints);
        Assert.False(settings.Watch.Enabled);

        // Null, not the default arrangement: absent means "never arranged", and what that turns into is the
        // application's call, made when it knows how big the window is.
        Assert.Null(settings.Layout);
    }

    [Fact]
    public void WhatIsSavedComesBack()
    {
        SettingsStore store = new(File0);

        store.Save(new UiSettings
        {
            Window = new WindowPlacement { Left = 100, Top = 50, Width = 1600, Height = 900, IsMaximized = true },
            Breakpoints = [new BreakpointMark { Stage = "Main", StepId = 3 }],
            Watch = new WatchSettings { Enabled = true, NotifyOnlyOnFailure = true },
            Layout = DockLayout.Default.Move(PanelId.Summary, DockSide.Right, 0).Resize(DockSide.Bottom, 0.35)
        });

        UiSettings read = store.Load();

        Assert.Equal(1600, read.Window!.Width);
        Assert.True(read.Window.IsMaximized);
        Assert.Equal("Main", Assert.Single(read.Breakpoints).Stage);
        Assert.Equal(3, read.Breakpoints[0].StepId);
        Assert.True(read.Watch.Enabled);
        Assert.True(read.Watch.NotifyOnlyOnFailure);
        // The arrangement survives the round trip through JSON, wells, floats and all — which is the whole
        // reason it is a plain record rather than anything WPF.
        Assert.Equal([PanelId.Summary, PanelId.Step], read.Layout!.At(DockSide.Right).Panels);
        Assert.Equal(0.35, read.Layout.At(DockSide.Bottom).Extent);
        Assert.Equal([PanelId.Runs, PanelId.Values], read.Layout.At(DockSide.Left).Panels);
    }

    [Fact]
    public void ACorruptFileReadsAsDefaultsAndIsReportedRatherThanThrown()
    {
        // The failure that matters. A tool that will not open because of its own settings file is
        // worse than a tool with no settings.
        Directory.CreateDirectory(folder);
        File.WriteAllText(File0, "{ this is not json");

        string? reported = null;
        UiSettings settings = new SettingsStore(File0, message => reported = message).Load();

        Assert.Equal(UiSettings.Defaults, settings);
        Assert.NotNull(reported);

        // Kept, not deleted: it is the only evidence of what went wrong, and the next save replaces it.
        Assert.True(File.Exists(File0));
    }

    [Fact]
    public void AnEmptyFileReadsAsDefaults()
    {
        // What a killed process leaves behind if a write is ever made non-atomic again.
        Directory.CreateDirectory(folder);
        File.WriteAllText(File0, string.Empty);

        Assert.Equal(UiSettings.Defaults, new SettingsStore(File0).Load());
    }

    [Fact]
    public void SavingCreatesTheFolderItNeeds()
    {
        // First run on a new machine: nothing under LocalAppData exists yet.
        Assert.False(Directory.Exists(folder));

        new SettingsStore(File0).Save(UiSettings.Defaults);

        Assert.True(File.Exists(File0));
    }

    [Fact]
    public void AnUnwritableLocationIsReportedRatherThanThrown()
    {
        // A path whose parent is a file, so creating the directory cannot succeed.
        Directory.CreateDirectory(folder);
        string blocker = Path.Combine(folder, "blocker");
        File.WriteAllText(blocker, "not a directory");

        string? reported = null;
        SettingsStore store = new(Path.Combine(blocker, "settings.json"), message => reported = message);

        store.Save(UiSettings.Defaults);

        Assert.NotNull(reported);
    }

    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        SettingsStore store = new(File0);

        store.Save(UiSettings.Defaults);

        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void TheVersionOnDiskIsAlwaysTheOneThisBuildWrites()
    {
        // Saving a settings object that came from an older file must not preserve the old version, or
        // a migration would keep firing for as long as the file lived.
        SettingsStore store = new(File0);

        store.Save(new UiSettings { Version = 0 });

        Assert.Equal(UiSettings.CurrentVersion, store.Load().Version);
    }

    [Fact]
    public void ANewerFileIsReadRatherThanDiscarded()
    {
        // A downgrade must not wipe what a newer build wrote. Unknown members are ignored by the
        // reader; the ones this build understands still apply.
        Directory.CreateDirectory(folder);
        File.WriteAllText(File0, """
            { "Version": 99, "Watch": { "Enabled": true }, "SomethingFromTheFuture": 12 }
            """);

        UiSettings settings = new SettingsStore(File0).Load();

        Assert.True(settings.Watch.Enabled);
    }
}
