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

namespace QuestPatcher
{
    public class MainWindow : Window
    {
        private TextBlock welcomeText;
        private TextBlock appNotInstalledText;
        private TextBlock javaNotInstalledText;
        private TextBlock questNotPluggedInText;
        private TextBlock multipleDevicesText;
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

        public Logger Logger { get; }

        public string DATA_PATH { get; }
        public string TEMP_PATH { get; }

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
            this.DebugBridge = new DebugBridge(this);
            this.moddingHandler = new ModdingHandler(this);
            this.modsManager = new ModsManager(this);

            this.Opened += onLoad;
            this.Closed += onClose;

#if DEBUG
            this.AttachDevTools();
#endif      
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            findComponents();
        }

        private async void onLoad(object? sender, EventArgs args)
        {
            welcomeText.Text += (" " + DebugBridge.APP_ID);

            try
            {
                string version = await moddingHandler.InvokeJavaAsync("-version");
                string trimmedVersion = version.Split("\n")[0].Substring(13);

                Logger.Information("Java version " + trimmedVersion);
            }   catch (Exception ex)
            {
                LoggingBox.Height = 200;
                javaNotInstalledText.IsVisible = true;
                Logger.Information("Java not found");
                Logger.Verbose(ex.ToString());
                return;
            }

            // First install the debug bridge if it is missing
            try
            {
                await DebugBridge.InstallIfMissing();
            }
            catch (Exception ex)
            {
                Logger.Fatal("An error occurred while installing ADB: " + ex.Message);
                Logger.Verbose(ex.ToString());
                return;
            }

            LogcatButton.Click += DebugBridge.onStartLogcatClick;
            openLogsButton.Click += DebugBridge.onOpenLogsClick;

            editAppIdButton.Click += onEditAppIdClick;
            newAppIdConfirmButton.Click += onConfirmNewAppIdClick;

            // Then we can check if the app is installed
            patchingPanel.IsVisible = true;

            string listResult = await DebugBridge.runCommandAsync("shell pm list packages {app-id}");
            if (listResult.Contains("no devices/emulators found"))
            {
                LoggingBox.Height = 200;
                questNotPluggedInText.IsVisible = true;
                return;
            }   else if(listResult.Contains("more than one device/emulator"))   {
                LoggingBox.Height = 200;
                multipleDevicesText.IsVisible = true;
                return;
            }   else if (listResult == "")  {
                LoggingBox.Height = 200;
                appNotInstalledText.IsVisible = true;
                return;
            }   else   {
                LoggingBox.Height = 213;
                appInstalledText.IsVisible = true;
            }

            await moddingHandler.CheckInstallStatus();

            if (moddingHandler.AppInfo.IsModded)
            {
                await switchToModMenu();
            }
            else
            {
                LoggingBox.Height = 155;
                startModding.IsVisible = true;
            }

            startModding.Click += onStartModdingClick;
            browseModsButton.Click += onBrowseForModsClick;

            AddHandler(DragDrop.DropEvent, onDragAndDrop);
        }

        private async Task switchToModMenu() {
            this.MaxHeight += 250;
            this.MinHeight += 250;
            this.LoggingBox.Height = 155;

            startModding.IsVisible = false;
            InstalledMods.IsVisible = true;
            
            await modsManager.LoadModsFromQuest();
        }

        private async void onStartModdingClick(object? sender, RoutedEventArgs args)
        {
            startModding.IsVisible = false;
            LoggingBox.Height = 215;

            try
            {
                await moddingHandler.startModdingProcess();
            }   catch(Exception ex)
            {
                Logger.Fatal("An error occurred while attempting to patch the game");
                Logger.Fatal(ex.Message);
                Logger.Verbose(ex.ToString());
                return;
            }

            await switchToModMenu();
        }

        private void onEditAppIdClick(object? sender, RoutedEventArgs args)
        {
            nonEditAppIdPanel.IsVisible = false;

            newAppIdBox.Text = DebugBridge.APP_ID;
            newAppIdBox.SelectAll();
            editAppIdPanel.IsVisible = true;
            this.MinHeight += 10;
        }

        private void onConfirmNewAppIdClick(object? sender, RoutedEventArgs args)
        {
            string newId = newAppIdBox.Text;

            Logger.Information("Changing app Id to " + newId);
            File.WriteAllText(DATA_PATH + "appId.txt", newId);

            restartApp();
        }

        private void restartApp()
        {
            Logger.Information("Restarting . . . ");
            Process process = new Process();
            process.StartInfo.FileName = AppDomain.CurrentDomain.BaseDirectory + (OperatingSystem.IsWindows() ? "QuestPatcher.exe" : "QuestPatcher");
            process.StartInfo.UseShellExecute = false;
            process.Start();

            Environment.Exit(0);
        }

        private void onClose(object? sender, EventArgs args)
        {
            Directory.Delete(TEMP_PATH, true);
            Logger.Verbose("QuestPatcher closing-------------------");
        }

        private async void onBrowseForModsClick(object? sender, RoutedEventArgs args) {
            // Show a browse dialogue to enter the path of the mod file
            OpenFileDialog fileDialog = new OpenFileDialog();
            fileDialog.AllowMultiple = false;

            FileDialogFilter filter = new FileDialogFilter();
            filter.Extensions.Add("qmod");
            filter.Name = "Quest Mods";

            fileDialog.Filters.Add(filter);
            string[] files = await fileDialog.ShowAsync(this);
            if(files.Length == 0) {
                return;
            }

            // Install the mod with that path
            await attemptInstall(files[0]);
        }

        private async void onDragAndDrop(object? sender, DragEventArgs args)
        {
            if(args.Data.Contains(DataFormats.FileNames))
            {
                foreach(string path in args.Data.GetFileNames())
                {
                    await attemptInstall(path);
                }
            }
        }

        private async Task attemptInstall(string path)
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

        private void findComponents()
        {
            appNotInstalledText = this.FindControl<TextBlock>("appNotInstalledText");
            javaNotInstalledText = this.FindControl<TextBlock>("javaNotInstalledText");
            questNotPluggedInText = this.FindControl<TextBlock>("questNotPluggedInText");
            multipleDevicesText = this.FindControl<TextBlock>("multipleDevicesText");
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
