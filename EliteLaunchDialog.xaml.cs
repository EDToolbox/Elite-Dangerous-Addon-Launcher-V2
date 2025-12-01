using Microsoft.Win32;
using Serilog;
using System;
using System.IO;
using System.Windows;

namespace Elite_Dangerous_Addon_Launcher_V2
{
    /// <summary>
    /// Dialog for selecting Elite Dangerous launch method and options.
    /// </summary>
    public partial class EliteLaunchDialog : Window
    {
        /// <summary>
        /// The selected path to EDLaunch.exe
        /// </summary>
        public string? SelectedPath { get; private set; }

        /// <summary>
        /// Whether to use Steam launch URL instead of direct path
        /// </summary>
        public bool UseSteamUrl { get; private set; }

        /// <summary>
        /// Whether to use Legendary launcher for Epic version
        /// </summary>
        public bool UseLegendary { get; private set; }

        /// <summary>
        /// Launch arguments based on selected options
        /// </summary>
        public string LaunchArguments { get; private set; } = string.Empty;

        /// <summary>
        /// Dialog result indicating if user confirmed selection
        /// </summary>
        public bool Confirmed { get; private set; }

        public EliteLaunchDialog()
        {
            InitializeComponent();
        }

        private void UpdateLaunchArguments()
        {
            var args = new System.Collections.Generic.List<string>();

            if (ChkAutoRun.IsChecked == true)
                args.Add("/autorun");

            if (ChkAutoQuit.IsChecked == true)
                args.Add("/autoexit");

            if (ChkVRMode.IsChecked == true)
                args.Add("/vr");

            LaunchArguments = string.Join(" ", args);
        }

        private void SetSelectedPath(string? path, string source)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                SelectedPath = path;
                BtnOK.IsEnabled = true;
                TxtStatus.Text = $"Found: {path}";
                Log.Information("Elite Dangerous selected via {Source}: {Path}", source, path);
            }
            else
            {
                TxtStatus.Text = "Elite Dangerous not found.";
                Log.Warning("Elite Dangerous not found via {Source}", source);
            }
        }

        private async void BtnStandardInstall_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Scanning for Elite Dangerous...";
            BtnStandardInstall.IsEnabled = false;

            try
            {
                var paths = await MainWindow.ScanComputerForEdLaunch();
                if (paths.Count > 0)
                {
                    SetSelectedPath(paths[0], "Standard Scan");
                }
                else
                {
                    TxtStatus.Text = "Elite Dangerous not found on this computer.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during standard installation scan");
                TxtStatus.Text = "Error during scan. Please try manual browse.";
            }
            finally
            {
                BtnStandardInstall.IsEnabled = true;
            }
        }

        private void BtnSteam_Click(object sender, RoutedEventArgs e)
        {
            // Use Steam URL for launching
            UseSteamUrl = true;
            SelectedPath = "steam://rungameid/359320";
            BtnOK.IsEnabled = true;
            TxtStatus.Text = "Steam launch configured.";
            Log.Information("Elite Dangerous configured for Steam launch");
        }

        private void BtnEpic_Click(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Searching Epic Games installation...";

            try
            {
                // Search Epic manifests for Elite Dangerous
                var epicPath = FindEliteInEpic();
                if (!string.IsNullOrEmpty(epicPath))
                {
                    SetSelectedPath(epicPath, "Epic Games");
                }
                else
                {
                    TxtStatus.Text = "Elite Dangerous not found in Epic Games. Try Legendary or Browse.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error searching Epic Games installation");
                TxtStatus.Text = "Error searching Epic. Please try manual browse.";
            }
        }

        private void BtnLegendary_Click(object sender, RoutedEventArgs e)
        {
            // Check if Legendary is installed
            if (!IsLegendaryInstalled())
            {
                MessageBox.Show(
                    "Legendary is not found in PATH.\n\nPlease install Legendary from:\nhttps://github.com/derrod/legendary",
                    "Legendary Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Search for Elite via Epic/Legendary
            var epicPath = FindEliteInEpic();
            if (!string.IsNullOrEmpty(epicPath))
            {
                UseLegendary = true;
                SetSelectedPath(epicPath, "Legendary");
                TxtStatus.Text = "Legendary launcher configured for Epic version.";
            }
            else
            {
                TxtStatus.Text = "Elite Dangerous not found in Epic Games library.";
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select EDLaunch.exe",
                Filter = "Elite Dangerous Launcher|EDLaunch.exe|All Executables|*.exe",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                SetSelectedPath(dialog.FileName, "Manual Browse");
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            UpdateLaunchArguments();
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private static string? FindEliteInEpic()
        {
            const string manifestDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

            if (!Directory.Exists(manifestDir))
                return null;

            foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var manifest = Newtonsoft.Json.Linq.JObject.Parse(json);

                    string? displayName = manifest["DisplayName"]?.ToString();
                    string? installLocation = manifest["InstallLocation"]?.ToString();

                    if (!string.IsNullOrEmpty(displayName) &&
                        displayName.Contains("Elite Dangerous", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(installLocation))
                    {
                        var edLaunchPath = Path.Combine(installLocation, AppConstants.EdLaunchExe);
                        if (File.Exists(edLaunchPath))
                        {
                            return edLaunchPath;
                        }
                    }
                }
                catch
                {
                    // Skip invalid manifests
                }
            }

            return null;
        }

        private static bool IsLegendaryInstalled()
        {
            try
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var paths = pathEnv.Split(Path.PathSeparator);

                foreach (var path in paths)
                {
                    var legendaryPath = Path.Combine(path, "legendary.exe");
                    if (File.Exists(legendaryPath))
                        return true;
                }

                // Also check common locations
                var commonPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "legendary", "legendary.exe"),
                    @"C:\Program Files\legendary\legendary.exe",
                    @"C:\Program Files (x86)\legendary\legendary.exe"
                };

                foreach (var path in commonPaths)
                {
                    if (File.Exists(path))
                        return true;
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }
    }
}
