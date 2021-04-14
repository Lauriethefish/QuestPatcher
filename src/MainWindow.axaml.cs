using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using System.IO;
using Serilog.Events;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;

namespace QuestPatcher
{
    public class MainWindow : Window
    {
        private TextBlock welcomeText;
        private TextBlock appNotInstalledText;
        private TextBlock javaNotInstalledText;
        private TextBlock questNotPluggedInText;
        private TextBlock multipleDevicesText;
        private TextBlock unauthorizedText;
        private TextBlock otherErrorText;
        private TextBlock appInstalledText;
        public TextBox LoggingBox { get; private set; }
        private Button startModding;
        private Panel patchingPanel;
        public TextBlock ModInstallErrorText { get; private set; }

        private Button browseModsButton;
        public ScrollViewer InstalledMods { get; private set; }
        public StackPanel InstalledModsPanel { get; private set; }

        public Button LogcatButton { get; private set; }
        private Button openLogsButton;

        private Button editAppIdButton;
        private TextBox newAppIdBox;
        private Button newAppIdConfirmButton;
        private StackPanel nonEditAppIdPanel;
        private StackPanel editAppIdPanel;

        public AppInfo AppInfo { get; private set; }

        public DebugBridge DebugBridge { get; private set; }
        private ModdingHandler moddingHandler;

        private ModsManager modsManager;

        // Map of file extension (upper case) to location on the Quest, loaded from drag-and-drop.json
        private Dictionary<string, FileCopyTypeInfo> fileCopyPaths = new Dictionary<string, FileCopyTypeInfo>();

        public Logger Logger { get; }

        public string DATA_PATH { get; }
        public string TEMP_PATH { get; }

        public ConfigManager ConfigManager { get; }
        public Config Config { get; private set; }

        private string CONFIG_PATH;

        public MainWindow()
        {
            DATA_PATH = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/QuestPatcher/";
            TEMP_PATH = Path.GetTempPath() + "/QuestPatcher/";

            Directory.CreateDirectory(DATA_PATH);

            Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(LogEventLevel.Verbose)
                .WriteTo.File(DATA_PATH + "log.log", LogEventLevel.Verbose, "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.TextWriter(new WindowLogger(this), LogEventLevel.Information, "{Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Logger.Verbose("QuestPatcher starting-------------------");

            InitializeComponent();
            ConfigManager = new ConfigManager(Logger, DATA_PATH);
            this.DebugBridge = new DebugBridge(this);
            this.moddingHandler = new ModdingHandler(this);
            this.modsManager = new ModsManager(this);

            this.Opened += OnLoad;
            this.Closed += OnClose;

#if DEBUG
            this.AttachDevTools();
#endif      
        }

        private async Task LoadFileCopyPaths()
        {
            Logger.Verbose("Loading file copy paths . . .");
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream? fileCopiesStream = assembly.GetManifestResourceStream("QuestPatcher.resources.file-copy-paths.json");
            if(fileCopiesStream == null)
            {
                throw new FileNotFoundException("Unable to find file copy paths in resources");
            }

            JsonDocument document = await JsonDocument.ParseAsync(fileCopiesStream);
            JsonElement packageElement;
            if(document.RootElement.TryGetProperty(Config.AppId, out packageElement))
            {
                foreach(JsonProperty property in packageElement.EnumerateObject())
                {
                    fileCopyPaths[property.Name.ToUpper()] = new FileCopyTypeInfo(property.Value);
                }
            }

            Logger.Verbose("Loaded " + fileCopyPaths.Count + " copy paths");
        }

        private async void OnLoad(object? sender, EventArgs args)
        {
            try
            {
                await OnLoadInternal();
            }
            catch (Exception ex)
            {
                LoggingBox.Height = 200;
                otherErrorText.IsVisible = true;
                Logger.Error("An unhandled error occured while loading QuestPatcher");
                Logger.Error(ex.ToString());
            }
        }

        private async Task OnLoadInternal()
        {
            // Make sure the config is loaded, e.g. to get the correct app ID for ADB
            await ConfigManager.LoadConfig();
            Config = ConfigManager.Config;

            welcomeText.Text += (" " + Config.AppId);
            openLogsButton.Click += DebugBridge.OnOpenLogsClick;

            await LoadFileCopyPaths();
            await VerifyToolsVersions();
          
            // If Java and ADB are installed, then these buttons are safe to click
            LogcatButton.Click += DebugBridge.OnStartLogcatClick;
            editAppIdButton.Click += OnEditAppIdClick;
            newAppIdConfirmButton.Click += OnConfirmNewAppIdClick;

            // Now we can check if the app is installed
            if(!await CheckAppInstallStatus())
            {
                LoggingBox.Height = 200;
                return;
            }

            await moddingHandler.CheckInstallStatus();

            if (moddingHandler.AppInfo.IsModded)
            {
                await SwitchToModMenu();
            }
            else
            {
                PrepareForPatching();
            }
        }

        private async Task<bool> CheckAppInstallStatus()
        {
            patchingPanel.IsVisible = true;

            string listResult = await DebugBridge.RunCommandAsync("shell pm list packages {app-id}");
            // Check ADB errors like multiple devices, or unauthorized access (didn't hit confirm in headset)
            if (listResult.Contains("no devices/emulators found"))
            {
                LoggingBox.Height = 200;
                questNotPluggedInText.IsVisible = true;
                return false;
            }
            else if (listResult.Contains("more than one device/emulator"))
            {
                LoggingBox.Height = 200;
                multipleDevicesText.IsVisible = true;
                return false;
            }
            else if (listResult.Contains("unauthorized"))
            {
                LoggingBox.Height = 200;
                unauthorizedText.IsVisible = true;
                return false;
            }
            else if (listResult == "") // No apps are listed with the configured ID
            {
                LoggingBox.Height = 200;
                appNotInstalledText.IsVisible = true;
                return false;
            }
            else
            {
                LoggingBox.Height = 213;
                appInstalledText.IsVisible = true;
                return true;
            }
        }

        private void PrepareForPatching()
        {
            LoggingBox.Height = 155;

            startModding.IsVisible = true;
            startModding.Click += OnStartModdingClick;
        }

        // Verifies that Java and ADB are installed. Returns true if successful, false otherwise
        // Will download ADB if not on PATH
        private async Task<bool> VerifyToolsVersions() {
            // Check that Java is installed
            try
            {
                string version = await moddingHandler.InvokeJavaAsync("-version");
                string trimmedVersion = version.Split("\n")[0].Substring(13);

                Logger.Information("Java version " + trimmedVersion);
            }
            catch (Exception ex)
            {
                LoggingBox.Height = 200;
                javaNotInstalledText.IsVisible = true;
                Logger.Information("Java not found");
                Logger.Verbose(ex.ToString());
                return false;
            }

            // Install/set the path to the debug bridge.
            try
            {
                await DebugBridge.InstallIfMissing();
            }
            catch (Exception ex)
            {
                Logger.Fatal("An error occurred while installing ADB: " + ex.Message);
                Logger.Verbose(ex.ToString());
                return false;
            }
            return true;
        }

        private async Task SwitchToModMenu() {
            this.MaxHeight += 250;
            this.MinHeight += 250;
            this.LoggingBox.Height = 155;

            startModding.IsVisible = false;
            InstalledMods.IsVisible = true;

            browseModsButton.Click += OnBrowseForModsClick;
            AddHandler(DragDrop.DropEvent, OnDragAndDrop);
            await modsManager.LoadModsFromQuest();
        }

        private async void OnStartModdingClick(object? sender, RoutedEventArgs args)
        {
            startModding.IsVisible = false;
            LoggingBox.Height = 215;

            try
            {
                await moddingHandler.StartModdingProcess();
            }   catch(Exception ex)
            {
                Logger.Fatal("An error occurred while attempting to patch the game");
                Logger.Fatal(ex.Message);
                Logger.Verbose(ex.ToString());
                return;
            }

            await SwitchToModMenu();
        }

        private void OnEditAppIdClick(object? sender, RoutedEventArgs args)
        {
            nonEditAppIdPanel.IsVisible = false;

            newAppIdBox.Text = Config.AppId;
            newAppIdBox.SelectAll();
            editAppIdPanel.IsVisible = true;
            this.MinHeight += 10;
        }

        private async void OnConfirmNewAppIdClick(object? sender, RoutedEventArgs args)
        {
            string newId = newAppIdBox.Text;

            Logger.Information("Changing app Id to " + newId);
            Config.AppId = newId;
            await ConfigManager.SaveConfig();

            RestartApp();
        }

        // Restarts QuestPatcher from the installation directory.
        // Used when changing app IDs
        private void RestartApp()
        {
            Logger.Information("Restarting . . . ");
            Process process = new Process();
            process.StartInfo.FileName = AppDomain.CurrentDomain.BaseDirectory + (OperatingSystem.IsWindows() ? "QuestPatcher.exe" : "QuestPatcher");
            process.StartInfo.UseShellExecute = false;
            process.Start();

            Environment.Exit(0);
        }

        private void OnClose(object? sender, EventArgs args)
        {
            // Clear temp files in order to save on disk space.
            try
            {
                Directory.Delete(TEMP_PATH, true);
            }
            catch (Exception)
            {
                Logger.Warning("Failed to delete the temporary directory - is it still in use?");
            }
            Logger.Verbose("QuestPatcher closing-------------------");
        }

        private async void OnBrowseForModsClick(object? sender, RoutedEventArgs args) {
            // Show a browse dialogue to enter the path of the mod file
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.AllowMultiple = false;

            FileDialogFilter filter = new FileDialogFilter();
            filter.Extensions.Add("qmod");
            filter.Name = "Quest Mods";
            fileDialog.Filters.Add(filter);

            foreach (KeyValuePair<string, FileCopyTypeInfo> fileCopy in fileCopyPaths)
            {
                FileDialogFilter fileCopyFilter = new FileDialogFilter();
                fileCopyFilter.Extensions.Add(fileCopy.Key.ToLower());

                string description = fileCopy.Value.Description == null ? fileCopy.Key + " Files" : fileCopy.Value.Description;
                fileCopyFilter.Name = description;
                fileDialog.Filters.Add(fileCopyFilter);
            }

            string[] files = await fileDialog.ShowAsync(this);
            if(files == null || files.Length == 0) {
                return;
            }

            // Install the mod with that path
            await AttemptInstall(files[0]);
        }

        private async void OnDragAndDrop(object? sender, DragEventArgs args)
        {
            if(args.Data.Contains(DataFormats.FileNames))
            {
                // Sometimes a COMException gets thrown if the files can't be parsed for whatever reason.
                // We need to handle this to avoid crashing QuestPatcher.
                try
                {
                    IEnumerable<string> fileNames = args.Data.GetFileNames();
                }
                catch (COMException)
                {
                    Logger.Error("Invalid file dragged into window");
                    return;
                }

                foreach(string path in args.Data.GetFileNames())
                {
                    await AttemptInstall(path);
                }
            }
        }

        // Attempts a file copy or a mod install of the file at path
        // If neither is available for the file, it'll print an error message
        private async Task AttemptInstall(string path)
        {
            string extension = Path.GetExtension(path).Substring(1).ToUpper(); // Remove the . and make the extension upper case
            if(extension == "QMOD")
            {
                await AttemptInstallMod(path);
            }
            else
            {
                FileCopyTypeInfo copyPath;
                if(fileCopyPaths.TryGetValue(extension, out copyPath))
                {
                    await AttemptFileCopy(path, copyPath.DestinationPath);
                }
                else
                {
                    Logger.Error("Unknown file extension " + extension);
                }
            }
        }

        private async Task AttemptInstallMod(string path)
        {
            try
            {
                ModInstallErrorText.IsVisible = false;
                await modsManager.InstallMod(path);
            }
            catch (Exception ex)
            {
                ModInstallErrorText.IsVisible = true;
                ModInstallErrorText.Text = "Error while installing mod: " + ex.Message;
                Logger.Verbose(ex.ToString());
            }
        }

        private async Task AttemptFileCopy(string path, string destination)
        {
            try
            {
                Logger.Information("Copying file . . .");
                ModInstallErrorText.IsVisible = false;
                await DebugBridge.RunCommandAsync("push \"" + path + "\" \"" + Path.Combine(destination, Path.GetFileName(path)) + "\"");
                Logger.Information("Successfully copied " + Path.GetFileName(path) + " to " + destination);
            }
            catch (Exception ex)
            {
                ModInstallErrorText.IsVisible = true;
                ModInstallErrorText.Text = "Error while copying file: " + ex.Message;
                Logger.Verbose(ex.ToString());
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            FindComponents();
        }

        // Assigns all of the controls that have a field in this class
        private void FindComponents()
        {
            appNotInstalledText = this.FindControl<TextBlock>("appNotInstalledText");
            javaNotInstalledText = this.FindControl<TextBlock>("javaNotInstalledText");
            questNotPluggedInText = this.FindControl<TextBlock>("questNotPluggedInText");
            multipleDevicesText = this.FindControl<TextBlock>("multipleDevicesText");
            unauthorizedText = this.FindControl<TextBlock>("unauthorizedText");
            otherErrorText = this.FindControl<TextBlock>("otherErrorText");
            appInstalledText = this.FindControl<TextBlock>("appInstalledText");
            LoggingBox = this.FindControl<TextBox>("loggingBox");
            startModding = this.FindControl<Button>("startModding");
            welcomeText = this.FindControl<TextBlock>("welcomeText");
            InstalledModsPanel = this.FindControl<StackPanel>("installedModsPanel");
            InstalledMods = this.FindControl<ScrollViewer>("installedMods");
            browseModsButton = this.FindControl<Button>("browseModsButton");
            ModInstallErrorText = this.FindControl<TextBlock>("modInstallErrorText");
            patchingPanel = this.FindControl<Panel>("patchingPanel");
            LogcatButton = this.FindControl<Button>("logcatButton");
            openLogsButton = this.FindControl<Button>("openLogsButton");
            editAppIdButton = this.FindControl<Button>("editAppIdButton");
            nonEditAppIdPanel = this.FindControl<StackPanel>("nonEditAppIdPanel");
            editAppIdPanel = this.FindControl<StackPanel>("editAppIdPanel");
            newAppIdConfirmButton = this.FindControl<Button>("newAppIdConfirmButton");
            newAppIdBox = this.FindControl<TextBox>("newAppIdBox");
        }
    }
}
