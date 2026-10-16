using System;
using System.IO;
using TestFramework.Core.Debugger;
using TestFramework.DebugUI.State.Diagnostics;
using TestFramework.DebugUI.State.Shell;

namespace TestFramework.DebugUI.State.Settings;

/// <summary>
/// Whether test runs on this machine are recorded, and the one place that changes it.
/// </summary>
/// <remarks>
/// <para>
/// Recording is on exactly when Core's marker file, <see cref="DebugJournal.RecordingMarkerName"/>,
/// exists inside the journal root. This touches that file and nothing else: turning recording off
/// keeps every run recorded so far, because the runs live beside the marker rather than behind it.
/// </para>
/// <para>
/// Decided here, on the tool's side, and never by a test. Core reads the marker once per test process,
/// so a change applies to test runs started afterwards; a test host already running keeps the answer it
/// read when it started.
/// </para>
/// </remarks>
/// <param name="root">The journal root the marker lives in.</param>
public sealed class RecordingSwitch(string root)
{
    /// <summary>The journal root, whether or not it exists.</summary>
    public string Root { get; } = string.IsNullOrWhiteSpace(root)
        ? throw new ArgumentException("A journal root is required.", nameof(root))
        : root;

    /// <summary>The file whose existence switches recording on.</summary>
    public string Marker => Path.Combine(Root, DebugJournal.RecordingMarkerName);

    /// <summary>Whether recording is on right now. Read from disk on every call.</summary>
    public bool IsOn => File.Exists(Marker);

    /// <summary>
    /// The switch for the journal root Core uses on this machine, or null when that cannot be told.
    /// </summary>
    public static RecordingSwitch? ForThisMachine()
    {
        try
        {
            return new RecordingSwitch(DebugJournal.Root);
        }
        catch (Exception e)
        {
            Log.Write(e);
            return null;
        }
    }

    /// <summary>Switches recording on, creating the journal root when it is missing.</summary>
    public void TurnOn()
    {
        Directory.CreateDirectory(Root);

        using FileStream marker = new(Marker, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
    }

    /// <summary>Switches recording off. Removes the marker only; recorded runs stay.</summary>
    public void TurnOff()
    {
        if (File.Exists(Marker))
            File.Delete(Marker);
    }

    /// <summary>What the switch says right now, for the state.</summary>
    public RecordingState Read() => new() { IsOn = IsOn, Root = Root };
}
