using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using TestFramework.DebugUI.State.Bundles;

namespace TestFramework.DebugUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    /// <remarks>
    /// One instance runs and later launches hand their work to it. The tool owns a named pipe that test hosts
    /// connect to, and a second copy would lose that pipe and then look like it was listening while every run
    /// went to the first — so the election happens here, before a window exists.
    /// </remarks>
    public partial class App : Application
    {
        /// <summary>How long a second launch waits for the running one to take its argument.</summary>
        private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(3);

        private SingleInstance? instance;

        /// <inheritdoc />
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (MainWindow is not null)
            {
                return;
            }

            string? bundle = BundleIn(e?.Args);

            instance = SingleInstance.Acquire();

            if (!instance.IsOwner)
            {
                // Hand over and go. If nobody answers - the owner died between the election and now - carry on
                // and start normally rather than exiting and leaving the reader with nothing.
                if (SingleInstance.TrySend(bundle, HandoverTimeout))
                {
                    instance.Dispose();
                    instance = null;

                    Shutdown();
                    return;
                }
            }

            MainWindow window = new();
            MainWindow = window;

            instance.Activated += window.OpenFromAnotherLaunch;
            instance.Listen();

            window.Show();

            // After the window exists, so anything it reports about the file has somewhere to be reported.
            if (bundle is not null)
                window.OpenFromAnotherLaunch(bundle);

            // Only repairs an association that is already there. The launcher installs each version into its own
            // folder, so yesterday's registration points at yesterday's executable.
            if (Environment.ProcessPath is { Length: > 0 } path)
                FileAssociation.EnsureCurrent(path);
        }

        /// <inheritdoc />
        protected override void OnExit(ExitEventArgs e)
        {
            instance?.Dispose();
            instance = null;

            base.OnExit(e);
        }

        /// <summary>
        /// The run bundle among the command line arguments, if there is one.
        /// </summary>
        /// <remarks>
        /// Recognised by extension rather than by position, so a shell that adds its own arguments cannot make
        /// the tool treat something else as a bundle.
        /// </remarks>
        private static string? BundleIn(string[]? args)
        {
            if (args is null)
                return null;

            string? found = args.FirstOrDefault(argument =>
                argument.EndsWith(BundleFormat.Extension, StringComparison.OrdinalIgnoreCase));

            if (found is null)
                return null;

            Debug.WriteLineIf(!System.IO.File.Exists(found), $"asked to open a bundle that is not there: {found}");

            return found;
        }
    }
}
