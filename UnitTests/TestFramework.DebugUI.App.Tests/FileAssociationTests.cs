using System;
using TestFramework.DebugUI;
using TestFramework.DebugUI.State.Bundles;

namespace TestFramework.DebugUI.App.Tests;

/// <summary>
/// Covers the command Explorer would be given.
/// </summary>
/// <remarks>
/// The registry writes themselves are not tested: they need a real registry, and what they do is four
/// <c>SetValue</c> calls. What is worth pinning down is the command string, because the executable lives under a
/// versioned folder full of dots and a path with a space in it is ordinary — get the quoting wrong and the file
/// never arrives, which looks like the import being broken rather than the association.
/// </remarks>
public class FileAssociationTests
{
    [Fact]
    public void BothThePathAndTheFileAreQuoted()
    {
        Assert.Equal(
            "\"C:\\Program Files\\Test Framework\\0.5.1\\TestFramework.DebugUI.exe\" \"%1\"",
            FileAssociation.CommandFor(@"C:\Program Files\Test Framework\0.5.1\TestFramework.DebugUI.exe"));
    }

    [Fact]
    public void TheFilePlaceholderIsThere()
    {
        // Without it Explorer starts the tool and never says which file was double-clicked.
        Assert.Contains("\"%1\"", FileAssociation.CommandFor(@"C:\x\app.exe"), StringComparison.Ordinal);
    }

    [Fact]
    public void ACommandNeedsSomethingToRun()
    {
        Assert.Throws<ArgumentException>(() => FileAssociation.CommandFor(string.Empty));
        Assert.Throws<ArgumentException>(() => FileAssociation.CommandFor("   "));
    }

    [Fact]
    public void TheTypeIsNamedAfterThisToolRatherThanTheExtension()
    {
        // A program identifier shared with another application would have them fighting over the type. It is
        // also what makes "is this registered to us" answerable at all.
        Assert.Equal("TestFramework.DebugUI.Run", FileAssociation.ProgId);
        Assert.Contains("DebugUI", FileAssociation.ProgId, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExtensionRegisteredIsTheOneBundlesAreWrittenWith()
    {
        // Two places naming the extension separately is how the association ends up on a file type nobody
        // produces. Both read it from the format.
        Assert.Equal(".tfrun", BundleFormat.Extension);
        Assert.StartsWith(".", BundleFormat.Extension, StringComparison.Ordinal);
    }
}
