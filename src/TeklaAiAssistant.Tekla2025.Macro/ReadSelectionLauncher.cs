// Tekla Structures 2025 launcher. Runtime execution in Tekla is not yet proven.
#pragma warning disable 1633 // Tekla's macro compiler handles reference directives.
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "System.Windows.Forms"
#pragma warning restore 1633

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace UserMacros
{
    public sealed class Macro
    {
        // Set this to the deployed host's absolute Windows path before use.
        // This local configuration does not change the user's environment.
        private const string HostExecutablePath = @"";

        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(HostExecutablePath)
                    || !Path.IsPathRooted(HostExecutablePath)
                    || Path.GetPathRoot(HostExecutablePath).Length < 3
                    || !File.Exists(HostExecutablePath))
                {
                    MessageBox.Show(
                        "Set HostExecutablePath in ReadSelectionLauncher.cs to the deployed host's absolute Windows executable path.",
                        "Tekla AI Assistant",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = HostExecutablePath,
                    Arguments = "--show-result",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(HostExecutablePath)
                }))
                {
                    if (process == null)
                        throw new InvalidOperationException("No host process was created.");
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "The host could not be started: " + exception.Message,
                    "Tekla AI Assistant",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
