using System;
using System.Diagnostics;
using System.Windows;

namespace TestFramework.DebugUI.Copying;

/// <summary>
/// Puts text on the clipboard, or quietly fails to.
/// </summary>
/// <remarks>
/// One place, because the clipboard is genuinely allowed to refuse: another process can hold it open, and
/// the call then throws. Losing a copy is a disappointment; taking down the window that was showing someone
/// their failure, because they tried to copy the failure, is not a trade worth making.
/// </remarks>
public static class Clipboards
{
    /// <summary>Copies text, reporting whether it arrived.</summary>
    public static bool Set(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return false;
        }
    }

    /// <summary>
    /// The text a field is worth copying as.
    /// </summary>
    /// <remarks>
    /// Trailing whitespace goes, because these come from labels that were padded for layout, and a value
    /// pasted into a shell or a filter with a stray space on the end is a value that does not match.
    /// Internal line breaks stay: a stack trace and a laid-out JSON body are worth having as they were.
    /// </remarks>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Normalised to the line ending the clipboard's consumers expect on this platform.
        string normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        return string.Join(Environment.NewLine, normalised.Split('\n')).TrimEnd();
    }
}
