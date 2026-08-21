using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TestFramework.DebugUI.State.Bundles;

namespace TestFramework.DebugUI;

/// <summary>
/// Makes a shared run open in this tool when it is double-clicked.
/// </summary>
/// <remarks>
/// <para>
/// Written under the current user, never the machine. Associating a file type for everyone needs administrator
/// rights and changes something for people who never asked; the per-user keys need no elevation and affect only
/// the account that turned it on.
/// </para>
/// <para>
/// Off until asked for. A tool that claims a file extension the first time it runs is a tool that has taken a
/// decision on someone's behalf, so this is a switch in the settings panel and the switch reads its state from
/// the registry rather than from a setting of its own — one source of truth, and the answer stays right if the
/// association is changed from outside.
/// </para>
/// </remarks>
public static class FileAssociation
{
    /// <summary>The identifier the extension points at.</summary>
    public const string ProgId = "TestFramework.DebugUI.Run";

    /// <summary>What Explorer calls the file type.</summary>
    public const string FriendlyName = "Test Framework run";

    private const string ClassesKey = @"Software\Classes";

    /// <summary>
    /// The command Explorer runs, with the file it was given.
    /// </summary>
    /// <remarks>
    /// Both parts quoted. The executable lives under a versioned folder whose name contains dots, and a path
    /// with a space in it is ordinary — unquoted, either becomes several arguments and the file never arrives.
    /// </remarks>
    public static string CommandFor(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        return $"\"{executablePath}\" \"%1\"";
    }

    /// <summary>Whether this tool is what opens a run bundle for this user.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using RegistryKey? extension = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{BundleFormat.Extension}");

            return string.Equals(extension?.GetValue(null) as string, ProgId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);

            return false;
        }
    }

    /// <summary>The command currently registered, which may be a previous version's.</summary>
    public static string? RegisteredCommand()
    {
        try
        {
            using RegistryKey? command = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{ProgId}\shell\open\command");

            return command?.GetValue(null) as string;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);

            return null;
        }
    }

    /// <summary>
    /// Claims the extension for this executable.
    /// </summary>
    /// <returns>Whether it worked.</returns>
    public static bool Register(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            using (RegistryKey type = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{ProgId}"))
            {
                type.SetValue(null, FriendlyName);

                using (RegistryKey icon = type.CreateSubKey("DefaultIcon"))
                    icon.SetValue(null, $"\"{executablePath}\",0");

                using (RegistryKey command = type.CreateSubKey(@"shell\open\command"))
                    command.SetValue(null, CommandFor(executablePath));
            }

            using (RegistryKey extension = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{BundleFormat.Extension}"))
                extension.SetValue(null, ProgId);

            Announce();

            return true;
        }
        catch (Exception e)
        {
            // A locked-down account can refuse even its own classes key. Losing the association is a smaller
            // problem than a settings toggle that takes the window down.
            Debug.WriteLine(e);

            return false;
        }
    }

    /// <summary>
    /// Gives the extension back.
    /// </summary>
    /// <remarks>
    /// Both keys go. Leaving the type behind with nothing pointing at it would show a stale entry in Explorer's
    /// "open with" list long after the tool was told to let go.
    /// </remarks>
    public static bool Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{BundleFormat.Extension}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{ProgId}", throwOnMissingSubKey: false);

            Announce();

            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);

            return false;
        }
    }

    /// <summary>
    /// Repairs the association after an update, and does nothing if there is none.
    /// </summary>
    /// <remarks>
    /// The launcher installs each version into its own folder, so the path registered yesterday points at the
    /// version that was current yesterday. Rather than register something that cannot go stale — there is no
    /// such path — the running version quietly takes ownership of an association that already exists. An
    /// association nobody asked for is still never created.
    /// </remarks>
    public static void EnsureCurrent(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !IsRegistered())
            return;

        if (string.Equals(RegisteredCommand(), CommandFor(executablePath), StringComparison.OrdinalIgnoreCase))
            return;

        Register(executablePath);
    }

    /// <summary>Tells the shell an association changed, so Explorer does not need restarting.</summary>
    private static void Announce()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
