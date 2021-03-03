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

namespace QuestPatcher
{
    public class MainWindow : Window
    {
        private TextBlock welcomeText;
        private TextBlock appNotInstalledText;
        private TextBlock questNotPluggedInText;
        private TextBlock appInstalledText;
        public TextBox LoggingBox { get; private set; }
        private Button startModding;
        private Panel patchingPanel;
        public TextBlock ModInstallErrorText { get; private set; }

        private Button browseModsButton;
        public ScrollViewer InstalledMods { get; private set; }
        public StackPanel InstalledModsPanel { get; private set; }

        public AppInfo AppInfo { get; private set; }

        public DebugBridge DebugBridge { get; private set; }
        private ModdingHandler moddingHandler;

        private ModsManager modsManager;

        public Logger Logger { get; }

        public string DATA_PATH { get; }

        public MainWindow()
        {
            DATA_PATH = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/QuestPatcher";
            Directory.CreateDirectory(DATA_PATH);

            Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(LogEventLevel.Verbose)
                .WriteTo.File(DATA_PATH + "/log.log", LogEventLevel.Verbose, "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.TextWriter(new WindowLogger(this), LogEventLevel.Information, "{Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Logger.Verbose("QuestPatcher starting-------------------");

            this.DebugBridge = new DebugBridge(this);
            this.moddingHandler = new ModdingHandler(this);
            this.modsManager = new ModsManager(this);

            this.Opened += onLoad;
            this.Closed += onClose;
            InitializeComponent();

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

            // Then we can check if the app is installed
            patchingPanel.IsVisible = true;

            string listResult = await DebugBridge.runCommandAsync("shell pm list packages {app-id}");
            if (listResult.Contains("no devices/emulators found"))
            {
                questNotPluggedInText.IsVisible = true;
                LoggingBox.Height = 240;
                return;
            }   else if (listResult == "")  {
                LoggingBox.Height = 240;
                appNotInstalledText.IsVisible = true;
                return;
            }   else   {
                LoggingBox.Height = 250;
                appInstalledText.IsVisible = true;
            }

            await moddingHandler.CheckInstallStatus();

            if (moddingHandler.AppInfo.IsModded)
            {
                await switchToModMenu();
            }
            else
            {
                LoggingBox.Height = 195;
                startModding.IsVisible = true;
            }

            startModding.Click += onStartModdingClick;
            browseModsButton.Click += onBrowseForModsClick;

            AddHandler(DragDrop.DropEvent, onDragAndDrop);
        }

        private async Task switchToModMenu() {
            this.MaxHeight += 200;
            this.MinHeight += 200;
            this.LoggingBox.Height = 145;

            startModding.IsVisible = false;
            InstalledMods.IsVisible = true;
            
            await modsManager.LoadModsFromQuest();
        }

        private async void onStartModdingClick(object? sender, RoutedEventArgs args)
        {
            startModding.IsVisible = false;
            LoggingBox.Height = 255;

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

        private void onClose(object? sender, EventArgs args)
        {
            moddingHandler.RemoveTemporaryDirectory();
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
            questNotPluggedInText = this.FindControl<TextBlock>("questNotPluggedInText");
            appInstalledText = this.FindControl<TextBlock>("appInstalledText");
            LoggingBox = this.FindControl<TextBox>("loggingBox");
            startModding = this.FindControl<Button>("startModding");
            welcomeText = this.FindControl<TextBlock>("welcomeText");
            InstalledModsPanel = this.FindControl<StackPanel>("installedModsPanel");
            InstalledMods = this.FindControl<ScrollViewer>("installedMods");
            browseModsButton = this.FindControl<Button>("browseModsButton");
            ModInstallErrorText = this.FindControl<TextBlock>("modInstallErrorText");
            patchingPanel = this.FindControl<Panel>("patchingPanel");
        }
    }
}
