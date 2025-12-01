using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using Serilog;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Elite_Dangerous_Addon_Launcher_V2.Services;
using Newtonsoft.Json.Linq;

namespace Elite_Dangerous_Addon_Launcher_V2

{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Private Fields

        private readonly object _processListLock = new object();
        private List<string> processList = new List<string>();

        // Cache for Epic game checks to avoid repeated manifest scans
        private static readonly Dictionary<string, bool> _epicInstallCache = new Dictionary<string, bool>();
        private static readonly object _epicCacheLock = new object();

        private bool _isChecking = false;
        private string _appVersion = string.Empty;
        private bool _isLoading = true;
        private string logpath = string.Empty;

        private bool isDarkTheme = false;
        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private Settings settings = new();

        #endregion Private Fields

        #region Public Properties

        public string ApplicationVersion
        {
            get { return _appVersion; }
            set
            {
                if (_appVersion != value)
                {
                    _appVersion = value;
                    OnPropertyChanged(nameof(ApplicationVersion));
                }
            }
        }

        #endregion Public Properties

        #region Public Constructors

        public MainWindow(string profileName = null)
        {
            InitializeComponent();
            LoggingConfig.Configure();
            if (!string.IsNullOrEmpty(profileName))
            {
                // Use the profileName to load the appropriate profile
                //LoadProfile(profileName);
            }
            // Copy user settings from previous application version if necessary
            if (Properties.Settings.Default.UpdateSettings)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpdateSettings = false;
                Properties.Settings.Default.Save();
            }
            this.SizeToContent = SizeToContent.Manual;
            this.Width = Properties.Settings.Default.MainWindowSize.Width;
            this.Height = Properties.Settings.Default.MainWindowSize.Height;
            this.Top = Properties.Settings.Default.MainWindowLocation.Y;
            this.Left = Properties.Settings.Default.MainWindowLocation.X;
            // Assign the event handler to the Loaded event
            this.Loaded += MainWindow_Loaded;
            // Use InformationalVersion for display (supports semantic versioning like 2.0.0-beta1)
            var infoVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
            {
                // Remove git hash suffix if present (e.g., "2.0.0-beta1+abc123" -> "2.0.0-beta1")
                var plusIndex = infoVersion.IndexOf('+');
                ApplicationVersion = plusIndex > 0 ? infoVersion.Substring(0, plusIndex) : infoVersion;
            }
            else
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                ApplicationVersion = $"{version?.Major}.{version?.Minor}.{version?.Build}";
            }
            // Set the data context to AppState instance
            this.DataContext = AppState.Instance;
            CloseAllAppsCheckbox.IsChecked = Properties.Settings.Default.CloseAllAppsOnExit;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            Properties.Settings.Default.MainWindowSize = new System.Drawing.Size((int)this.Width, (int)this.Height);
            Properties.Settings.Default.MainWindowLocation = new System.Drawing.Point((int)this.Left, (int)this.Top);
            Properties.Settings.Default.Save();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            if (Properties.Settings.Default.MainWindowLocation == new System.Drawing.Point(0, 0))
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        #endregion Public Constructors

        #region Public Methods

        public event PropertyChangedEventHandler? PropertyChanged;

        public Profile? CurrentProfile { get; set; }

        public List<Profile> OtherProfiles
        {
            get
            {
                if (Profiles == null || CurrentProfile == null)
                {
                    return new List<Profile>();
                }

                var otherProfiles = Profiles.Except(new List<Profile> { CurrentProfile }).ToList();
                Debug.WriteLine($"OtherProfiles: {string.Join(", ", otherProfiles.Select(p => p.Name))}");
                return otherProfiles;
            }
        }

        public List<Profile>? Profiles { get; set; }

        public new void DragOver(IDropInfo dropInfo)
        {
            MyApp sourceItem = dropInfo.Data as MyApp;
            MyApp targetItem = dropInfo.TargetItem as MyApp;

            if (sourceItem != null && targetItem != null)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                dropInfo.Effects = DragDropEffects.Move;
              
                _ = SaveProfilesAsync();
            }
        }

        public async Task LoadProfilesAsync(string profileName = null)
        {
            try
            {
                string localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string filePath = Path.Combine(localFolder, AppConstants.ProfilesFileName);

                if (File.Exists(filePath))
                {
                    // Read the file to a string
                    string json = await File.ReadAllTextAsync(filePath);

                    // Deserialize the JSON string to a list of profiles
                    List<Profile> loadedProfiles = JsonConvert.DeserializeObject<List<Profile>>(json);

                    // Convert list to ObservableCollection
                    var profiles = new ObservableCollection<Profile>(loadedProfiles);

                    // Set the loaded profiles to AppState.Instance.Profiles
                    AppState.Instance.Profiles = profiles;

                    if (profileName != null)
                    {
                        // If profileName argument is provided, select that profile
                        AppState.Instance.CurrentProfile = AppState.Instance.Profiles.FirstOrDefault(p => p.Name == profileName);
                        Cb_Profiles.SelectedItem = AppState.Instance.Profiles.FirstOrDefault(p => p.Name == profileName);
                    }
                    else
                    {
                        // Set the CurrentProfile to the default profile (or first one if no default exists)
                        AppState.Instance.CurrentProfile = AppState.Instance.Profiles.FirstOrDefault(p => p.IsDefault)
                            ?? AppState.Instance.Profiles.FirstOrDefault();
                        Cb_Profiles.SelectedItem = AppState.Instance.Profiles.FirstOrDefault(p => p.IsDefault);
                    }
                    SubscribeToAppEvents(AppState.Instance.CurrentProfile);
                }
                else
                {
                    // The profiles file doesn't exist, initialize Profiles with an empty collection
                    AppState.Instance.Profiles = new ObservableCollection<Profile>();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading profiles");
            }
        }

        public async Task SaveProfilesAsync()
        {
            // Serialize the profiles into a JSON string
            var profilesJson = JsonConvert.SerializeObject(AppState.Instance.Profiles);

            // Create a file called "profiles.json" in the local folder
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.ProfilesFileName);

            // Write the JSON string to the file
            await File.WriteAllTextAsync(path, profilesJson);
            AdjustWindowSizeToContent();
        }

        public void UpdateDataGrid()
        {
            // Set DataGrid's ItemsSource to the apps of the currently selected profile
            if (AppState.Instance.CurrentProfile != null)
            {
                AddonDataGrid.ItemsSource = AppState.Instance.CurrentProfile.Apps;
            }
            else
            {
                // No profile is selected. Clear the data grid.
                AddonDataGrid.ItemsSource = null;
            }

            // Resize the window to fit content
            AdjustWindowSizeToContent();
        }
        private void AdjustWindowSizeToContent()
        {
            // Give the UI time to update
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Calculate the required height based on the number of items in the grid
                    double headerHeight = 150;  // Space for controls at top
                    double footerHeight = 50;   // Increased padding at bottom for less cramped look
                    double rowHeight = 48;      // Approximate height of each row

                    int itemCount = 0;
                    if (AppState.Instance.CurrentProfile?.Apps != null)
                    {
                        itemCount = AppState.Instance.CurrentProfile.Apps.Count;
                    }

                    // Calculate the required window height
                    double requiredHeight = headerHeight + (itemCount * rowHeight) + footerHeight;

                    // Set minimum height
                    double minHeight = 300;
                    requiredHeight = Math.Max(requiredHeight, minHeight);

                    // Set maximum height to avoid excessively tall windows
                    double maxHeight = 800;
                    requiredHeight = Math.Min(requiredHeight, maxHeight);

                    // Set the window height directly
                    this.Height = requiredHeight;

                    // Ensure window is within screen bounds
                    if (this.Top + this.Height > SystemParameters.VirtualScreenHeight)
                    {
                        this.Top = Math.Max(0, SystemParameters.VirtualScreenHeight - this.Height);
                    }

                    // Force layout update
                    this.UpdateLayout();

                    Log.Information($"Window resized to height: {this.Height} for {itemCount} items");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error adjusting window size");
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion Public Methods

        #region Private Methods

        private void AddonDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Write the MyApp instances to the data source here, e.g.: SaveAppStateToFile();
        }

        private void ApplyTheme(string themeName)
        {
            var darkThemeUri = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml");
            var lightThemeUri = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml");

            var themeUri = themeName == "Dark" ? darkThemeUri : lightThemeUri;

            var existingTheme = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == themeUri);
            if (existingTheme == null)
            {
                existingTheme = new ResourceDictionary() { Source = themeUri };
                Application.Current.Resources.MergedDictionaries.Add(existingTheme);
            }

            // Remove the current theme
            var currentTheme = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == (themeName == "Dark" ? lightThemeUri : darkThemeUri));
            if (currentTheme != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(currentTheme);
            }
        }

        private void Bt_AddApp_Click_1(object sender, RoutedEventArgs e)
        {
            if (AppState.Instance.CurrentProfile != null)
            {
                AddApp addAppWindow = new AddApp()
                {
                    MainPageReference = this,
                    SelectedProfile = AppState.Instance.CurrentProfile,
                };
                // Set the owner and startup location
                addAppWindow.Owner = this; // Or replace 'this' with reference to the main window
                addAppWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                addAppWindow.Show();
                AddonDataGrid.ItemsSource = AppState.Instance.CurrentProfile.Apps;
            }
            else
            {
                // Handle the case when no profile is selected.
            }
        }

        private void Bt_AddProfile_Click_1(object sender, RoutedEventArgs e)
        {
            var window = new AddProfileDialog();
            // Center the dialog within the owner window
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Owner = this;  // Or replace 'this' with reference to the main window

            if (window.ShowDialog() == true)
            {
                string profileName = window.ProfileName;
                var newProfile = new Profile { Name = profileName };
                AppState.Instance.Profiles.Add(newProfile);

                AppState.Instance.CurrentProfile = newProfile;

                _ = SaveProfilesAsync().ConfigureAwait(false);
                UpdateDataGrid();
            }
        }

        private async void Bt_RemoveProfile_Click_1(object sender, RoutedEventArgs e)
        {
            Profile profileToRemove = (Profile)Cb_Profiles.SelectedItem;

            if (profileToRemove != null)
            {
                CustomDialog dialog = new CustomDialog("Are you sure you want to delete this profile?");
                dialog.Owner = Application.Current.MainWindow;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowDialog();

                if (dialog.Result == MessageBoxResult.Yes)
                {
                    AppState.Instance.Profiles.Remove(profileToRemove);

                    // Check if another profile can be selected
                    if (AppState.Instance.Profiles.Any())
                    {
                        // Select the next profile, or the first one if no next profile exists
                        AppState.Instance.CurrentProfile = AppState.Instance.Profiles.FirstOrDefault(p => p != profileToRemove) ?? AppState.Instance.Profiles.First();

                        // If no profile is set as default, set the current profile as the default
                        if (!AppState.Instance.Profiles.Any(p => p.IsDefault))
                        {
                            AppState.Instance.CurrentProfile.IsDefault = true;
                            DefaultCheckBox.IsChecked = true;
                        }
                    }
                    else
                    {
                        // No profiles left, so set CurrentProfile to null
                        AppState.Instance.CurrentProfile = null;
                    }

                    _ = SaveProfilesAsync().ConfigureAwait(false);
                    UpdateDataGrid();
                }
            }
        }

        private void Btn_Edit_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            MyApp appToEdit = button.CommandParameter as MyApp;

            if (appToEdit != null)
            {
                string exePath = Path.Combine(appToEdit.Path, appToEdit.ExeName);

                if (appToEdit.ExeName.Equals(AppConstants.EdLaunchExe, StringComparison.OrdinalIgnoreCase)
                    && IsEpicInstalled(exePath))
                {
                    // Open Legendary settings window instead of AddApp
                    var legendarySettings = new Views.LegendarySettingsWindow
                    {
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    legendarySettings.ShowDialog();
                    return;
                }

                // Fallback: regular AddApp editing
                AddApp addAppWindow = new AddApp
                {
                    AppToEdit = appToEdit,
                    MainPageReference = this
                };

                addAppWindow.Owner = this;
                addAppWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                addAppWindow.Title = "Edit App";
                addAppWindow.ShowDialog();
            }
        }

        public void ShowWhatsNewIfUpdated()
        {
            // Get the current assembly version.
            var assemblyVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            // Get the last seen version from the application settings.
            var lastSeenVersion = Properties.Settings.Default.LastSeenVersion;

            // If the last seen version is empty (which it will be the first time this method is run)
            // or if the assembly version is greater than the last seen version, show the what's new dialog.
            if (string.IsNullOrEmpty(lastSeenVersion) || new Version(lastSeenVersion) < assemblyVersion)
            {
                ShowWhatsNew();

                // Update the last seen version in the application settings.
                Properties.Settings.Default.LastSeenVersion = assemblyVersion.ToString();

                // Save the application settings.
                Properties.Settings.Default.Save();
            }
        }

        public void ShowWhatsNew()
        {
            WhatsNewWindow whatsNewWindow = new WhatsNewWindow();

            // Set the text to what's new
            Paragraph titleParagraph = new Paragraph();
            titleParagraph.Inlines.Add(new Bold(new Run("New for this version")));
            whatsNewWindow.WhatsNewText.Document.Blocks.Add(titleParagraph);

            List list = new List();

            ListItem listItem1 = new ListItem(new Paragraph(new Run("Fixed bug with renaming profiles causing a crash")));
            list.ListItems.Add(listItem1);

          //  ListItem listItem2 = new ListItem(new Paragraph(new Run("Profile Options for import/export and copy/rename/delete")));
          //  list.ListItems.Add(listItem2);


            whatsNewWindow.WhatsNewText.Document.Blocks.Add(list);

            whatsNewWindow.Owner = this; // Set owner to this MainWindow
            whatsNewWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner; // Center the window over its owner

            whatsNewWindow.ShowDialog();
        }








        private async void Btn_Launch_Click(object sender, RoutedEventArgs e)
        {
            // Code to launch all enabled apps
            if (AppState.Instance.CurrentProfile?.Apps == null)
            {
                Log.Warning("No profile or apps available");
                return;
            }

            Log.Information("Launching all enabled apps..");
            foreach (var app in AppState.Instance.CurrentProfile.Apps)
            {
                if (app.IsEnabled)
                {
                    Btn_Launch.IsEnabled = false;
                    await LaunchApp(app);
                    Log.Information("Launching {AppName}..", app.Name);
                }
            }
        }

        private void Cb_Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Cb_Profiles.SelectedItem is Profile selectedProfile)
            {
                // Select the currently selected profile
                AppState.Instance.CurrentProfile = selectedProfile;

                if (AppState.Instance.CurrentProfile != null)
                {
                    UpdateDataGrid();
                    DefaultCheckBox.IsChecked = selectedProfile.IsDefault;
                    if (!_isLoading)
                    {
                        _isChecking = true;
                        CheckEdLaunchInProfile();
                        _isChecking = false;
                    }
                }
            }
        }


        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)  // This is the checkbox for the default profile
        {
            var checkBox = (CheckBox)sender;
            var app = (MyApp)checkBox.Tag;
            if (app != null)
            {
                app.IsEnabled = false;
                _ = SaveProfilesAsync().ConfigureAwait(false);
            }
        }

        private async void CloseAllAppsCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            AppState.Instance.CloseAllAppsOnExit = true;
            settings.CloseAllAppsOnExit = true;
            await SaveSettingsAsync(settings);
        }

        private async void CloseAllAppsCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            AppState.Instance.CloseAllAppsOnExit = false;
            settings.CloseAllAppsOnExit = false;
            await SaveSettingsAsync(settings);
        }

        private void CopyToProfileSubMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Get the clicked MenuItem
            var menuItem = (MenuItem)sender;

            // Get the bounded MyApp item
            var boundedApp = (MyApp)((MenuItem)e.OriginalSource).DataContext;

            // Get the selected profile
            var selectedProfile = (Profile)menuItem.Tag;

            // Now you can copy boundedApp to selectedProfile.
        }

        private void DefaultCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (Cb_Profiles.SelectedItem is Profile selectedProfile)
            {
                var previouslyDefaultProfile = AppState.Instance.Profiles.FirstOrDefault(p => p.IsDefault);
                if (previouslyDefaultProfile != null)
                {
                    previouslyDefaultProfile.IsDefault = false;
                }
                selectedProfile.IsDefault = true;
                _ = SaveProfilesAsync().ConfigureAwait(false);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            // get the button that raised the event
            var button = (Button)sender;
            CustomDialog dialog = new CustomDialog("Are you sure?");
            dialog.Owner = Application.Current.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();

            if (dialog.Result == MessageBoxResult.Yes)
            {
                // retrieve the item associated with this button
                var appToDelete = (MyApp)button.DataContext;

                // remove the item from the collection
                try
                {
                    AppState.Instance.CurrentProfile.Apps.Remove(appToDelete);
                    Log.Information($"App {appToDelete.Name} deleted..", appToDelete.Name);
                }

                catch (Exception ex)
                {
                    // handle exception
                    Log.Error(ex, "An error occurred trying to delete an app..");
                }
                _ = SaveProfilesAsync().ConfigureAwait(false);
            }
        }

        private async Task LaunchApp(MyApp app)
        {
            string args;
            const string quote = "\"";
            var path = $"{app.Path}/{app.ExeName}";

            if (string.Equals(app.ExeName, AppConstants.TargetGuiExe, StringComparison.OrdinalIgnoreCase))
            {
                args = "-r " + quote + app.Args + quote;
            }
            else
            {
                args = app.Args;
            }
            // Handle .appref-ms ClickOnce apps
            if (app.ExeName.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutPath = Path.Combine(app.Path, app.ExeName);

                if (File.Exists(shortcutPath))
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = shortcutPath,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        // Use using statement for proper resource cleanup (optimization #4)
                        using (var process = Process.Start(psi))
                        {
                            // Process launched
                        }

                        // Attempt to track the actual ClickOnce process by expected name
                        string expectedExeName = Path.GetFileNameWithoutExtension(app.ExeName) + ".exe";
                        processList.Add(expectedExeName); // Add to list for shutdown tracking

                        UpdateStatus($"Launching {app.Name} via .appref-ms shortcut...");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Failed to launch {app.Name} via .appref-ms: {ex.Message}");
                    }
                }
                else
                {
                    UpdateStatus($"Shortcut not found: {shortcutPath}");
                }

                this.WindowState = WindowState.Minimized;
                return;
            }


            if (File.Exists(path))
            {
                try
                {
                    if (string.Equals(app.ExeName, AppConstants.EdLaunchExe, StringComparison.OrdinalIgnoreCase) &&
                        IsEpicInstalled(path))
                    {
                        if (!IsLegendaryInstalled())
                        {
                            MessageBox.Show("Elite Dangerous is installed via Epic Games, but 'legendary' is not found in PATH.\nPlease install it from https://github.com/derrod/legendary to enable Epic support.", "Legendary Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        LegendaryConfigManager.EnsureLegendaryConfig();

                        var psi = new ProcessStartInfo
                        {
                            FileName = "legendary",
                            Arguments = "launch elite",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        // Use using statement for proper resource cleanup (optimization #4)
                        using (var legendaryProc = Process.Start(psi))
                        {
                            string output = legendaryProc.StandardOutput.ReadToEnd();
                            string error = legendaryProc.StandardError.ReadToEnd();
                            Debug.WriteLine("Legendary output:\n" + output);
                            Debug.WriteLine("Legendary errors:\n" + error);
                        }

                        // Start a watcher for EDLaunch process
                        _ = Task.Run(async () =>
                        {
                            const int maxRetries = 20;
                            int retries = 0;

                            while (retries < maxRetries)
                            {
                                var edProc = Process.GetProcessesByName("EDLaunch").FirstOrDefault();
                                if (edProc != null)
                                {
                                    edProc.EnableRaisingEvents = true;
                                    edProc.Exited += (s, e) =>
                                    {
                                        Application.Current.Dispatcher.Invoke(() => ProcessExitHandler(s, e));
                                    };

                                    break;
                                }

                                await Task.Delay(500);
                                retries++;
                            }
                        }).ConfigureAwait(false);

                        UpdateStatus($"Launching {app.Name} (Epic) via Legendary...");
                        this.WindowState = WindowState.Minimized;
                        return;
                    }

                    // Default (non-Epic) launch
                    var info = new ProcessStartInfo(path)
                    {
                        Arguments = args,
                        UseShellExecute = false,
                        WorkingDirectory = app.Path,
                        CreateNoWindow = true
                    };

                    Process proc = Process.Start(info);
                    proc.EnableRaisingEvents = true;
                    processList.Add(proc.ProcessName);

                    if (proc.ProcessName.Equals("EDLaunch", StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Exited += new EventHandler(ProcessExitHandler);
                    }

                    await Task.Delay(50);
                    proc.Refresh();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"An error occurred trying to launch {app.Name}: {ex.Message}");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(app.WebAppURL))
                {
                    // Validate URL before launching (Security Fix #8)
                    if (!IsValidWebUrl(app.WebAppURL))
                    {
                        Log.Warning("Invalid or unsafe URL: {url}", app.WebAppURL);
                        UpdateStatus($"Invalid URL for {app.Name}");
                        return;
                    }

                    string target = app.WebAppURL;
                    // Use using statement for proper resource cleanup (optimization #4)
                    // UseShellExecute = false for security (Fix #1c)
                    using (var proc = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false }))
                    {
                        // Web app launched
                    }

                    if (target.Equals("steam://rungameid/359320", StringComparison.OrdinalIgnoreCase))
                    {
                        // Wait for EDLaunch process to start (with timeout)
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < 5000)
                        {
                            var edLaunchProc = Process.GetProcessesByName("EDLaunch").FirstOrDefault();
                            if (edLaunchProc != null)
                            {
                                edLaunchProc.EnableRaisingEvents = true;
                                edLaunchProc.Exited += new EventHandler(ProcessExitHandler);
                                break;
                            }
                            await Task.Delay(100);
                        }
                    }

                    UpdateStatus("Launching " + app.Name);
                }
                else
                {
                    UpdateStatus($"Unable to launch {app.Name}..");
                }
            }

            UpdateStatus("All apps launched, waiting for EDLaunch Exit..");
            this.WindowState = WindowState.Minimized;
        }
     

        private bool IsLegendaryInstalled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "legendary",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    process.WaitForExit(2000);
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<Settings> LoadSettingsAsync()
        {
            string localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string settingsFilePath = Path.Combine(localFolder, "settings.json");

            Settings settings;
            if (File.Exists(settingsFilePath))
            {
                string json = await File.ReadAllTextAsync(settingsFilePath);
                // Security Fix #8: Use safe JSON settings for deserialization
                var jsonSettings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    ConstructorHandling = ConstructorHandling.Default
                };
                settings = JsonConvert.DeserializeObject<Settings>(json, jsonSettings) ?? new Settings { Theme = AppConstants.DefaultTheme };
                if (settings == null)
                {
                    settings = new Settings { Theme = AppConstants.DefaultTheme };
                    Log.Warning("Settings file invalid, using defaults");
                }
            }
            else
            {
                // If the settings file doesn't exist, use defaults
                settings = new Settings { Theme = "Light" };
            }
            Log.Information("Settings loaded: {Settings}", settings);
            return settings;
        }
        private void Btn_LaunchSingle_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            MyApp? appToLaunch = button.CommandParameter as MyApp;

            if (appToLaunch != null)
            {
                // Disable the main launch button while an app is being launched
                Btn_Launch.IsEnabled = false;

                // Launch just this one app
                _ = LaunchApp(appToLaunch);
                Log.Information("Launching single app: {AppName}", appToLaunch.Name);

                // If this is not Elite Dangerous (which would keep the button disabled),
                // re-enable the launch button
                if (!appToLaunch.ExeName.Equals(AppConstants.EdLaunchExe, StringComparison.OrdinalIgnoreCase) &&
                    !appToLaunch.WebAppURL?.Contains("rungameid/359320") == true &&
                    !appToLaunch.WebAppURL?.Contains("epic://launch") == true &&
                    !appToLaunch.WebAppURL?.Contains("legendary://launch") == true)
                {
                    Btn_Launch.IsEnabled = true;
                }
            }
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            await LoadProfilesAsync(App.ProfileName);
            settings = await LoadSettingsAsync();
            isDarkTheme = settings.Theme == "Dark";
            ApplyTheme(settings.Theme);
            AppState.Instance.CloseAllAppsOnExit = settings.CloseAllAppsOnExit;

            // Check if there are no profiles and invoke AddProfileDialog if none exist
            if (AppState.Instance.Profiles == null || AppState.Instance.Profiles.Count == 0)
            {
                var window = new AddProfileDialog();
                // Center the dialog within the owner window
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = this;  // Or replace 'this' with reference to the main window

                if (window.ShowDialog() == true)
                {
                    string profileName = window.ProfileName;
                    var newProfile = new Profile { Name = profileName, IsDefault = true };

                    // Unmark any existing default profiles
                    if (AppState.Instance.Profiles != null)
                    {
                        foreach (var profile in AppState.Instance.Profiles)
                        {
                            profile.IsDefault = false;
                        }
                    }

                    AppState.Instance.Profiles?.Add(newProfile);
                    AppState.Instance.CurrentProfile = newProfile;

                    await SaveProfilesAsync();
                    UpdateDataGrid();
                }
            }

            if (App.AutoLaunch)
            {
                foreach (var app in AppState.Instance.CurrentProfile.Apps)
                {
                    if (app.IsEnabled)
                    {
                        _ = LaunchApp(app).ConfigureAwait(false);
                    }
                }
            }
            _isLoading = false;
            if (_isChecking == false)
            {
                _isChecking = true;
                CheckEdLaunchInProfile();
                _isChecking = false;
            }
            ShowWhatsNewIfUpdated();
        }



        private void ModifyTheme(Uri newThemeUri)
        {
            var appResources = Application.Current.Resources;
            var oldTheme = appResources.MergedDictionaries.FirstOrDefault(d => d.Source.ToString().Contains("MaterialDesignTheme.Light.xaml") || d.Source.ToString().Contains("MaterialDesignTheme.Dark.xaml"));

            if (oldTheme != null)
            {
                appResources.MergedDictionaries.Remove(oldTheme);
            }

            appResources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = newThemeUri });
        }

        private void MyApp_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MyApp.IsEnabled) || e.PropertyName == nameof(MyApp.Order))
            {
                _ = SaveProfilesAsync().ConfigureAwait(false);
            }
        }

        // maybe redundant now
        private void OnProfileChanged(Profile oldProfile, Profile newProfile)
        {
            UnsubscribeFromAppEvents(oldProfile);
            SubscribeToAppEvents(newProfile);
        }

        private void ProcessExitHandler(object sender, EventArgs e)  //triggered when EDLaunch exits
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                Btn_Launch.IsEnabled = true;
                bool closeAllApps = CloseAllAppsCheckbox.IsChecked == true;

                // if EDLaunch has quit, does the user want us to kill all the apps?
                if (closeAllApps)
                {
                    Log.Information("CloseAllAppsOnExit is enabled, closing all apps..");
                    try
                    {
                        foreach (string p in processList)
                        {
                            Log.Information("Closing {0}", p);
                            // Calculate once before loop (optimization #3)
                            string processName = Path.GetFileNameWithoutExtension(p);
                            foreach (Process proc in Process.GetProcessesByName(processName))
                            {
                                try
                                {
                                    proc.CloseMainWindow();
                                    proc.WaitForExit(5000); // give it time to exit
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning("Failed to close process {0}: {1}", p, ex.Message);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // if something went wrong, don't raise an exception
                        Log.Error("An error occurred trying to close all apps..");
                    }
                    // doesn't seem to want to kill VoiceAttack nicely..
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(AppConstants.VoiceAttackProcessName);
                        foreach (var proc in procs) { proc.Kill(); }        //sadly this means next time it starts, it will complain it was shutdown in an unclean fashion
                    }
                    catch
                    {
                        // if something went wrong, don't raise an exception
                    }
                    // Elite Dangerous Odyssey Materials Helper is a little strange, let's deal with its
                    // multiple running processes..
                    try
                    {
                        Process[] procs = Process.GetProcessesByName(AppConstants.EliteDangerousOdysseyHelperName);
                        foreach (var proc in procs) { proc.CloseMainWindow(); }
                    }
                    catch
                    {
                        // if something went wrong, don't raise an exception
                    }
                    // delay for 5 seconds then quit (non-blocking)
                    await Task.Delay(5000);
                    Environment.Exit(0);
                }
            });
        }


        private async Task SaveSettingsAsync(Settings settings)
        {
            string localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string settingsFilePath = Path.Combine(localFolder, "settings.json");

            string json = JsonConvert.SerializeObject(settings);
            await File.WriteAllTextAsync(settingsFilePath, json);
        }

        private void SubscribeToAppEvents(Profile profile)
        {
            if (profile != null)
            {
                foreach (var app in profile.Apps)
                {
                    app.PropertyChanged += (object? sender, PropertyChangedEventArgs e) => MyApp_PropertyChanged((MyApp?)sender, e);
                }
            }
        }

        private void ToggleThemeButton_Click(object sender, RoutedEventArgs e)
        {
            isDarkTheme = !isDarkTheme;

            settings.Theme = isDarkTheme ? "Dark" : "Light"; // <-- Add this
            _ = SaveSettingsAsync(settings);

            var darkThemeUri = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml");
            var lightThemeUri = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml");

            var themeUri = isDarkTheme ? darkThemeUri : lightThemeUri;

            System.Diagnostics.Debug.WriteLine("Before toggle:");
            foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
            {
                System.Diagnostics.Debug.WriteLine($" - {dictionary.Source}");
            }

            var existingTheme = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == themeUri);
            if (existingTheme == null)
            {
                existingTheme = new ResourceDictionary() { Source = themeUri };
                Application.Current.Resources.MergedDictionaries.Add(existingTheme);
            }

            // Remove the current theme
            var currentTheme = Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == (isDarkTheme ? lightThemeUri : darkThemeUri));
            if (currentTheme != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(currentTheme);
            }

            System.Diagnostics.Debug.WriteLine("After toggle:");
            foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
            {
                System.Diagnostics.Debug.WriteLine($" - {dictionary.Source}");
            }
            _ = SaveSettingsAsync(settings);
        }

        private void UnsubscribeFromAppEvents(Profile profile)
        {
            if (profile != null)
            {
                foreach (var app in profile.Apps)
                {
                    app.PropertyChanged -= (object? sender, PropertyChangedEventArgs e) => MyApp_PropertyChanged((MyApp?)sender, e);
                }
            }
        }

        private void UpdateStatus(string status)
        {
            // Define how you update the status in your application
        }

        #endregion Private Methods

        #region Steam Detection

        /// <summary>
        /// Gets the Steam installation path from the Windows Registry.
        /// </summary>
        private static string? GetSteamPath()
        {
            try
            {
                // Try 64-bit registry first
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                {
                    Log.Information("Found Steam path from registry (64-bit): {Path}", path);
                    return path;
                }

                // Try 32-bit registry
                using var key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key32?.GetValue("InstallPath") is string path32 && Directory.Exists(path32))
                {
                    Log.Information("Found Steam path from registry (32-bit): {Path}", path32);
                    return path32;
                }

                // Try current user registry
                using var keyUser = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (keyUser?.GetValue("SteamPath") is string pathUser && Directory.Exists(pathUser))
                {
                    Log.Information("Found Steam path from user registry: {Path}", pathUser);
                    return pathUser;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read Steam path from registry");
            }

            return null;
        }

        /// <summary>
        /// Parses Steam's libraryfolders.vdf to get all Steam library paths.
        /// </summary>
        private static List<string> GetSteamLibraryFolders(string steamPath)
        {
            var libraries = new List<string> { steamPath };
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (!File.Exists(vdfPath))
            {
                Log.Warning("Steam libraryfolders.vdf not found at: {Path}", vdfPath);
                return libraries;
            }

            try
            {
                var content = File.ReadAllText(vdfPath);
                
                // Parse VDF format - look for "path" entries
                // Format: "path"		"D:\\SteamLibrary"
                var pathPattern = new System.Text.RegularExpressions.Regex(
                    "\"path\"\\s*\"([^\"]+)\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in pathPattern.Matches(content))
                {
                    var libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(libraryPath) && !libraries.Contains(libraryPath, StringComparer.OrdinalIgnoreCase))
                    {
                        libraries.Add(libraryPath);
                        Log.Information("Found Steam library folder: {Path}", libraryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse Steam libraryfolders.vdf");
            }

            return libraries;
        }

        /// <summary>
        /// Searches for Elite Dangerous in all Steam library folders.
        /// Elite Dangerous Steam App ID: 359320
        /// </summary>
        private static string? FindEliteInSteamLibraries()
        {
            var steamPath = GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                Log.Information("Steam not found on this system");
                return null;
            }

            var libraries = GetSteamLibraryFolders(steamPath);
            const string eliteAppId = "359320";
            
            foreach (var library in libraries)
            {
                // Check for Elite Dangerous app manifest
                var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{eliteAppId}.acf");
                if (File.Exists(manifestPath))
                {
                    Log.Information("Found Elite Dangerous manifest: {Path}", manifestPath);
                    
                    // Parse manifest to get install directory
                    try
                    {
                        var manifestContent = File.ReadAllText(manifestPath);
                        var installDirPattern = new System.Text.RegularExpressions.Regex(
                            "\"installdir\"\\s*\"([^\"]+)\"",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        
                        var match = installDirPattern.Match(manifestContent);
                        if (match.Success)
                        {
                            var installDir = match.Groups[1].Value;
                            var fullPath = Path.Combine(library, "steamapps", "common", installDir);
                            var edLaunchPath = Path.Combine(fullPath, AppConstants.EdLaunchExe);
                            
                            if (File.Exists(edLaunchPath))
                            {
                                Log.Information("Found Elite Dangerous via Steam: {Path}", edLaunchPath);
                                return edLaunchPath;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to parse Elite Dangerous manifest");
                    }
                }

                // Fallback: Check common install location directly
                var commonPath = Path.Combine(library, "steamapps", "common", "Elite Dangerous", AppConstants.EdLaunchExe);
                if (File.Exists(commonPath))
                {
                    Log.Information("Found Elite Dangerous via direct path: {Path}", commonPath);
                    return commonPath;
                }
            }

            Log.Information("Elite Dangerous not found in any Steam library");
            return null;
        }

        #endregion Steam Detection

        #region Epic Games Detection

        /// <summary>
        /// Searches for Elite Dangerous in Epic Games Store installations.
        /// Parses manifest files in C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests
        /// </summary>
        private static string? FindEliteInEpicLibrary()
        {
            const string manifestDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
            
            if (!Directory.Exists(manifestDir))
            {
                Log.Information("Epic Games manifest directory not found");
                return null;
            }

            Log.Information("Searching Epic Games manifests...");

            foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
            {
                try
                {
                    // Validate file size before reading (Security)
                    const long MAX_MANIFEST_SIZE = 1 * 1024 * 1024; // 1MB
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > MAX_MANIFEST_SIZE)
                    {
                        continue;
                    }

                    var json = File.ReadAllText(file);
                    var manifest = JObject.Parse(json);
                    
                    string? displayName = manifest["DisplayName"]?.ToString();
                    string? installLocation = manifest["InstallLocation"]?.ToString();
                    string? launchExe = manifest["LaunchExecutable"]?.ToString();

                    // Check if this is Elite Dangerous
                    if (!string.IsNullOrEmpty(displayName) && 
                        displayName.Contains("Elite Dangerous", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(installLocation) &&
                        !string.IsNullOrEmpty(launchExe))
                    {
                        // Look for EDLaunch.exe in the install location
                        var edLaunchPath = Path.Combine(installLocation, AppConstants.EdLaunchExe);
                        if (File.Exists(edLaunchPath))
                        {
                            Log.Information("Found Elite Dangerous via Epic Games: {Path}", edLaunchPath);
                            return edLaunchPath;
                        }

                        // Also check subdirectories (some games have nested structures)
                        var subDirPath = Path.Combine(installLocation, "Elite Dangerous", AppConstants.EdLaunchExe);
                        if (File.Exists(subDirPath))
                        {
                            Log.Information("Found Elite Dangerous via Epic Games (subdirectory): {Path}", subDirPath);
                            return subDirPath;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Log.Warning("Invalid JSON in Epic manifest {file}: {message}", file, ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error reading Epic manifest {file}", file);
                }
            }

            Log.Information("Elite Dangerous not found in Epic Games library");
            return null;
        }

        #endregion Epic Games Detection

        #region Frontier Standalone Detection

        /// <summary>
        /// Searches for Elite Dangerous Frontier Standalone installation.
        /// Checks common installation paths and Windows Registry uninstall entries.
        /// </summary>
        private static string? FindEliteInFrontierStandalone()
        {
            Log.Information("Searching for Frontier Standalone installation...");

            // Common Frontier installation paths
            var commonPaths = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Frontier", "EDLaunch"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Frontier", "EDLaunch"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Frontier", "Elite Dangerous"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Frontier", "Elite Dangerous"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Frontier_Developments", "Products"),
                @"C:\Program Files (x86)\Frontier\EDLaunch",
                @"C:\Program Files\Frontier\EDLaunch",
                @"D:\Frontier\EDLaunch",
                @"D:\Games\Frontier\EDLaunch",
                @"E:\Frontier\EDLaunch",
                @"E:\Games\Frontier\EDLaunch",
            };

            // Check common paths first
            foreach (var basePath in commonPaths)
            {
                if (Directory.Exists(basePath))
                {
                    var edLaunchPath = Path.Combine(basePath, AppConstants.EdLaunchExe);
                    if (File.Exists(edLaunchPath))
                    {
                        Log.Information("Found Elite Dangerous via Frontier path: {Path}", edLaunchPath);
                        return edLaunchPath;
                    }

                    // Search subdirectories (Frontier sometimes uses version folders)
                    try
                    {
                        var found = Directory.GetFiles(basePath, AppConstants.EdLaunchExe, SearchOption.AllDirectories).FirstOrDefault();
                        if (!string.IsNullOrEmpty(found))
                        {
                            Log.Information("Found Elite Dangerous via Frontier subdirectory: {Path}", found);
                            return found;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error searching Frontier directory: {Path}", basePath);
                    }
                }
            }

            // Check Windows Registry for Frontier installations
            try
            {
                var registryPaths = new[]
                {
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Frontier Developments",
                    @"SOFTWARE\Frontier Developments"
                };

                foreach (var regPath in registryPaths)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            var displayName = subKey?.GetValue("DisplayName")?.ToString();
                            var installLocation = subKey?.GetValue("InstallLocation")?.ToString();

                            if (!string.IsNullOrEmpty(displayName) &&
                                displayName.Contains("Elite", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(installLocation) &&
                                Directory.Exists(installLocation))
                            {
                                var edLaunchPath = Path.Combine(installLocation, AppConstants.EdLaunchExe);
                                if (File.Exists(edLaunchPath))
                                {
                                    Log.Information("Found Elite Dangerous via Registry: {Path}", edLaunchPath);
                                    return edLaunchPath;
                                }

                                // Check subdirectories
                                var subFound = Directory.GetFiles(installLocation, AppConstants.EdLaunchExe, SearchOption.TopDirectoryOnly).FirstOrDefault();
                                if (!string.IsNullOrEmpty(subFound))
                                {
                                    Log.Information("Found Elite Dangerous via Registry subdirectory: {Path}", subFound);
                                    return subFound;
                                }
                            }
                        }
                        catch
                        {
                            // Skip inaccessible registry keys
                        }
                    }
                }

                // Also check HKCU for user-specific installations
                foreach (var regPath in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    using var key = Registry.CurrentUser.OpenSubKey(regPath);
                    if (key == null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            var displayName = subKey?.GetValue("DisplayName")?.ToString();
                            var installLocation = subKey?.GetValue("InstallLocation")?.ToString();

                            if (!string.IsNullOrEmpty(displayName) &&
                                displayName.Contains("Elite", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrEmpty(installLocation) &&
                                Directory.Exists(installLocation))
                            {
                                var edLaunchPath = Path.Combine(installLocation, AppConstants.EdLaunchExe);
                                if (File.Exists(edLaunchPath))
                                {
                                    Log.Information("Found Elite Dangerous via User Registry: {Path}", edLaunchPath);
                                    return edLaunchPath;
                                }
                            }
                        }
                        catch
                        {
                            // Skip inaccessible registry keys
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error searching Windows Registry for Elite Dangerous");
            }

            Log.Information("Elite Dangerous not found via Frontier Standalone detection");
            return null;
        }

        #endregion Frontier Standalone Detection

        public static async Task<List<string>> ScanComputerForEdLaunch()
        {
            List<string> foundPaths = new List<string>();
            
            // First, try Steam detection (fast)
            Log.Information("Attempting Steam library detection...");
            var steamPath = FindEliteInSteamLibraries();
            if (!string.IsNullOrEmpty(steamPath))
            {
                foundPaths.Add(steamPath);
                Log.Information("Elite Dangerous found via Steam detection: {Path}", steamPath);
                return foundPaths;
            }

            // Second, try Epic Games detection (fast)
            Log.Information("Attempting Epic Games detection...");
            var epicPath = FindEliteInEpicLibrary();
            if (!string.IsNullOrEmpty(epicPath))
            {
                foundPaths.Add(epicPath);
                Log.Information("Elite Dangerous found via Epic Games detection: {Path}", epicPath);
                return foundPaths;
            }

            // Third, try Frontier Standalone detection (fast)
            Log.Information("Attempting Frontier Standalone detection...");
            var frontierPath = FindEliteInFrontierStandalone();
            if (!string.IsNullOrEmpty(frontierPath))
            {
                foundPaths.Add(frontierPath);
                Log.Information("Elite Dangerous found via Frontier Standalone detection: {Path}", frontierPath);
                return foundPaths;
            }

            // All fast methods failed, fall back to full scan
            Log.Information("Steam, Epic, and Frontier detection failed, starting full disk scan...");
            
            string targetFolder = "Elite Dangerous";
            string targetFile = AppConstants.EdLaunchExe;
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            SearchProgressWindow progressWindow = new SearchProgressWindow();

            progressWindow.Owner = Application.Current.MainWindow;
            progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            progressWindow.Closing += (s, e) => tokenSource.Cancel();

            progressWindow.Show(); // Show the window before starting the task

            await Task.Run(() =>
            {
                try
                {
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        if (token.IsCancellationRequested)
                            break;

                        if (drive.DriveType == DriveType.Fixed)
                        {
                            try
                            {
                                string driveRoot = drive.RootDirectory.ToString();
                                if (TraverseDirectories(driveRoot, targetFolder, targetFile, foundPaths, progressWindow, 7, token))
                                {
                                    break;
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // If we don't have access to the directory, skip it
                            }
                            catch (IOException)
                            {
                                // If another error occurs, skip it
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // If operation is canceled, return
                    return;
                }
            }, token);

            if (progressWindow.IsVisible)
                progressWindow.Close();

            return foundPaths;
        }

        public static bool TraverseDirectories(string root, string targetFolder, string targetFile, List<string> foundPaths, SearchProgressWindow window, int maxDepth, CancellationToken token, int currentDepth = 0)
        {
            // Array of directories to exclude
            string[] excludeDirs = {
                "windows",
                "users",
                "OneDriveTemp",
                "ProgramData",
                "$Recycle.Bin",
                "OneDrive"
            };

            if (token.IsCancellationRequested)
                return false;

            // Make sure not to exceed maximum depth
            if (currentDepth > maxDepth) return false;

            foreach (string dir in Directory.GetDirectories(root))
            {
                // Check for excluded directories
                bool isExcluded = false;
                foreach (string excludeDir in excludeDirs)
                {
                    if (dir.ToLower().Contains(excludeDir.ToLower()))
                    {
                        isExcluded = true;
                        break;
                    }
                }

                if (isExcluded || token.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    string dirName = new DirectoryInfo(dir).Name;

                    if (dirName.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        // The folder has the name we're looking for, now we just need to check if
                        // the file is there. Use filter parameter for efficiency (optimization #2)
                        var files = Directory.GetFiles(dir, targetFile, SearchOption.TopDirectoryOnly);
                        if (files.Length > 0)
                        {
                            foundPaths.Add(files[0]);
                            return true; // File has been found
                        }
                    }

                    // Trim the path for display in the UI
                    string trimmedPath = dir;
                    if (dir.Count(f => f == '\\') > 2)
                    {
                        var parts = dir.Split('\\');
                        trimmedPath = string.Join("\\", parts.Take(3)) + "\\...";
                    }

                    window.Dispatcher.Invoke(() =>
                    {
                        window.searchStatusTextBlock.Text = $"Checking: {trimmedPath}";
                    });

                    // Move on to the next level
                    bool found = TraverseDirectories(dir, targetFolder, targetFile, foundPaths, window, maxDepth, token, currentDepth + 1);

                    if (found)
                    {
                        return true; // File has been found in a subdirectory, so we stop the search
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // If we don't have access to the directory, skip it
                }
                catch (IOException)
                {
                    // If another error occurs, skip it
                }
            }

            return false; // If we get to this point, we haven't found the file
        }

        private static string ShortenPath(string fullPath, int maxParts)
        {
            var parts = fullPath.Split(Path.DirectorySeparatorChar);
            return string.Join(Path.DirectorySeparatorChar, parts.Take(maxParts));
        }

        private void AddonDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var dataGrid = (DataGrid)sender;
            var originalSource = (DependencyObject)e.OriginalSource;

            // Find the DataGridRow in the visual tree
            while ((originalSource != null) && !(originalSource is DataGridRow))
            {
                originalSource = VisualTreeHelper.GetParent(originalSource);
            }

            // If we found a DataGridRow
            if (originalSource is DataGridRow dataGridRow)
            {
                // Get the MyApp instance from the row
                var app = (MyApp)dataGridRow.DataContext;
                AppState.Instance.CurrentApp = app;
            }
        }

        private void Bt_CopyProfile_Click(object sender, RoutedEventArgs e)
        {
            var currentProfile = AppState.Instance.CurrentProfile;

            if (currentProfile != null)
            {
                bool isUnique = false;
                string newName = currentProfile.Name;

                while (!isUnique)
                {
                    var dialog = new RenameProfileDialog(newName);
                    dialog.Title = "Copy Profile";
                    // Center the dialog within the owner window
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    dialog.Owner = this;  // Or replace 'this' with reference to the main window

                    if (dialog.ShowDialog() == true)
                    {
                        newName = dialog.NewName;

                        // Check if the profile name is unique
                        if (!AppState.Instance.Profiles.Any(p => p.Name == newName))
                        {
                            // Change profile name
                            var newProfile = new Profile
                            {
                                Name = newName,
                                Apps = new ObservableCollection<MyApp>(currentProfile.Apps.Select(app => app.DeepCopy()))
                            };
                            AppState.Instance.Profiles.Add(newProfile);
                            AppState.Instance.CurrentProfile = newProfile; // switch to the new profile
                            _ = SaveProfilesAsync().ConfigureAwait(false);
                            Cb_Profiles.SelectedItem = newProfile; // update combobox selected item
                            isUnique = true;
                        }
                        else
                        {
                            ErrorDialog errdialog = new ErrorDialog("Profile name must be unique. Please enter a different name.");
                            errdialog.Owner = Application.Current.MainWindow;
                            errdialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            errdialog.ShowDialog();
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        private void Bt_RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            var currentProfile = AppState.Instance.CurrentProfile;

            if (currentProfile != null)
            {
                // Show rename dialog and get result
                var dialog = new RenameProfileDialog(currentProfile.Name);
                // Center the dialog within the owner window
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.Owner = this;  // Or replace 'this' with reference to the main window

                if (dialog.ShowDialog() == true)
                {
                    // Change profile name
                    currentProfile.Name = dialog.NewName;
                    // need to save the settings
                    _ = SaveProfilesAsync().ConfigureAwait(false);
                }
            }
        }

        private async void CheckEdLaunchInProfile()
        {
            // Get the current profile
            var currentProfile = AppState.Instance.CurrentProfile;
            if (currentProfile == null)
            {
                return;
            }
            // Check if edlaunch.exe exists in the current profile
            if (!currentProfile.Apps.Any(a => a.ExeName.Equals(AppConstants.EdLaunchExe, StringComparison.OrdinalIgnoreCase)
                                || a.WebAppURL?.Equals("steam://rungameid/359320", StringComparison.OrdinalIgnoreCase) == true))
            {
                // edlaunch.exe does not exist in the current profile - show the Elite Launch Dialog
                var launchDialog = new EliteLaunchDialog
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                if (launchDialog.ShowDialog() == true && launchDialog.Confirmed)
                {
                    if (launchDialog.UseSteamUrl)
                    {
                        // Steam version - use Steam URL
                        var edlaunch = new MyApp
                        {
                            Name = "Elite Dangerous (Steam)",
                            ExeName = string.Empty,
                            Path = string.Empty,
                            WebAppURL = "steam://rungameid/359320",
                            Args = launchDialog.LaunchArguments,
                            IsEnabled = true,
                            Order = 0
                        };
                        currentProfile.Apps.Add(edlaunch);
                        await SaveProfilesAsync();
                        Log.Information("Added Elite Dangerous (Steam) to profile");
                    }
                    else if (!string.IsNullOrEmpty(launchDialog.SelectedPath))
                    {
                        // Direct path or Legendary
                        string? edLaunchDir = Path.GetDirectoryName(launchDialog.SelectedPath);
                        if (!string.IsNullOrEmpty(edLaunchDir))
                        {
                            string name = "Elite Dangerous";
                            if (launchDialog.UseLegendary)
                            {
                                name = "Elite Dangerous (Epic/Legendary)";
                            }

                            var edlaunch = new MyApp
                            {
                                Name = name,
                                ExeName = AppConstants.EdLaunchExe,
                                Path = edLaunchDir,
                                Args = launchDialog.LaunchArguments,
                                IsEnabled = true,
                                Order = 0
                            };
                            currentProfile.Apps.Add(edlaunch);
                            await SaveProfilesAsync();
                            Log.Information("Added {Name} to profile: {Path}", name, edLaunchDir);
                        }
                    }
                }
            }
        }

        private void CloseAllAppsCheckbox_Checked_1(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.CloseAllAppsOnExit = true;
            Properties.Settings.Default.Save();
        }

        private void CloseAllAppsCheckbox_Unchecked_1(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.CloseAllAppsOnExit = false;
            Properties.Settings.Default.Save();
        }

        private DataGridRow? GetDataGridRow(MenuItem menuItem)
        {
            DependencyObject? obj = menuItem;
            while (obj != null && obj.GetType() != typeof(DataGridRow))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            return obj as DataGridRow;
        }

        private void ProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)e.OriginalSource;
            var profile = (Profile)menuItem.DataContext;
            var app = AppState.Instance.CurrentApp;
            Debug.WriteLine($"Clicked on profile: {profile.Name}");

            // Now you can use profile and app. Make sure to create a copy of app, not to use the
            // same instance in multiple profiles.
            if (app != null)
            {
                Debug.WriteLine($"Clicked on app: {app.Name}");
                var appCopy = new MyApp
                {
                    Args = app.Args,
                    ExeName = app.ExeName,
                    InstallationURL = app.InstallationURL,
                    IsEnabled = app.IsEnabled,
                    Name = app.Name,
                    Order = app.Order,
                    Path = app.Path,
                    WebAppURL = app.WebAppURL
                };

                // Add the app to the profile
                profile.Apps.Add(appCopy);
                _ = SaveProfilesAsync().ConfigureAwait(false);
            }
            else
            {
                Debug.WriteLine("No app selected");
            }
        }
        private void ExportProfiles(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "JSON file (*.json)|*.json";
            saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (saveFileDialog.ShowDialog() == true)
            {
                string json = JsonConvert.SerializeObject(AppState.Instance.Profiles);
                File.WriteAllText(saveFileDialog.FileName, json);
            }
        }
        private async void ImportProfiles(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON file (*.json)|*.json";
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (openFileDialog.ShowDialog() == true)
            {
                string json = File.ReadAllText(openFileDialog.FileName);
                // Security Fix #8: Use safe JSON settings for deserialization
                var jsonSettings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    ConstructorHandling = ConstructorHandling.Default
                };
                var importedProfiles = JsonConvert.DeserializeObject<List<Profile>>(json, jsonSettings);
                
                if (importedProfiles == null || importedProfiles.Count == 0)
                {
                    MessageBox.Show("No profiles found in the imported file.", "Import Failed");
                    return;
                }
                
                CustomDialog dialog = new CustomDialog("Are you sure you, this will remove all current profiles?");
                dialog.Owner = Application.Current.MainWindow;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowDialog();

                if (dialog.Result == MessageBoxResult.No)
                {
                      return;
                }
                    // remove current profiles
                    AppState.Instance.Profiles.Clear();

                // add imported profiles
                foreach (var profile in importedProfiles)
                {
                    AppState.Instance.Profiles.Add(profile);
                }

                // save the changes
                _ = SaveProfilesAsync().ConfigureAwait(false);

                // update the UI
                UpdateDataGrid();
                Cb_Profiles.SelectedIndex = 0; // if you want to automatically select the first imported profile
            }
        }
        /// <summary>
        /// Validates that a full path is within the expected base directory (Security Fix #3)
        /// </summary>
        private bool IsPathWithinBasePath(string fullPath, string basePath)
        {
            try
            {
                var fullInfo = new FileInfo(Path.GetFullPath(fullPath));
                var baseInfo = new DirectoryInfo(Path.GetFullPath(basePath));
                
                // Check if file is within base directory
                return fullInfo.FullName.StartsWith(
                    baseInfo.FullName + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates URL before opening (Security Fix #8)
        /// </summary>
        private bool IsValidWebUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            
            // Only allow http/https for web URLs
            return uri.Scheme is "http" or "https";
        }

        private bool IsEpicInstalled(string exePath)
        {
            // Check cache first (optimization #1)
            lock (_epicCacheLock)
            {
                if (_epicInstallCache.TryGetValue(exePath, out bool cached))
                {
                    return cached;
                }
            }

            string manifestDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
            bool result = false;

            if (Directory.Exists(manifestDir))
            {
                string exeFileName = Path.GetFileName(exePath);
                string? exeDirectory = Path.GetDirectoryName(exePath);

                foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
                {
                    try
                    {
                        // Validate file size before reading (Security Fix #5)
                        const long MAX_MANIFEST_SIZE = 1 * 1024 * 1024; // 1MB
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Length > MAX_MANIFEST_SIZE)
                        {
                            Log.Warning("Manifest file too large: {file}", file);
                            continue;
                        }

                        var json = File.ReadAllText(file);
                        var manifest = JObject.Parse(json);
                        string? launchExe = manifest["LaunchExecutable"]?.ToString();
                        string? installLocation = manifest["InstallLocation"]?.ToString();

                        if (!string.IsNullOrEmpty(installLocation) && !string.IsNullOrEmpty(launchExe))
                        {
                            // Compare the full exe path
                            string expectedFullPath = Path.Combine(installLocation, launchExe);

                            if (string.Equals(Path.GetFullPath(expectedFullPath), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                            {
                                result = true;
                                break;
                            }

                            // Or compare just folder + filename
                            if (!string.IsNullOrEmpty(exeDirectory) && string.Equals(Path.GetFullPath(installLocation), Path.GetFullPath(exeDirectory), StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(launchExe, exeFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                result = true;
                                break;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Log.Warning("Invalid JSON in manifest {file}: {message}", file, ex.Message);
                    }
                    catch (IOException ex)
                    {
                        Log.Warning("Cannot read manifest {file}: {message}", file, ex.Message);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log.Error("Access denied to manifest {file}: {message}", file, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error reading manifest {file}", file);
                    }
                }
            }

            // Cache the result
            lock (_epicCacheLock)
            {
                _epicInstallCache[exePath] = result;
            }

            return result;
        }

        private void Btn_ShowLogs(object sender, RoutedEventArgs e)
        {
            logpath = LoggingConfig.logFileFullPath ?? "";
            // Ensure the directory exists
            try
            {
                // Get the most recent log file

                if (!string.IsNullOrEmpty(logpath) && File.Exists(logpath))
                {
                    try
                    {
                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = logpath,
                            UseShellExecute = true
                        };

                        // Use using statement for proper resource cleanup (optimization #4)
                        using (Process.Start(processStartInfo))
                        {
                            // Log file opened in default viewer
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error opening log file: {ex.Message}");
                    }
                }
                else
                {
                    Log.Error("Log file does not exist.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error opening log file: {ex.Message}");
            }
        }
    }
}