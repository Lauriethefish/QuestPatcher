using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Threading.Tasks;

namespace QuestPatcher
{
    public class MainWindow : Window
    {
        private TextBlock welcomeText;
        private TextBlock appNotInstalledText;
        private TextBlock appInstalledText;
        private TextBox loggingBox;
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

        private bool firstActivation = true;

        public MainWindow()
        {
            this.DebugBridge = new DebugBridge(this);
            this.moddingHandler = new ModdingHandler(this);
            this.modsManager = new ModsManager(this);

            this.Activated += onLoad;
            this.Closed += onClose;
            InitializeComponent();

#if DEBUG
            this.AttachDevTools();
#endif      
        }

        // Writes a new line to the "modding log" section
        public void log(string str)
        {
            Console.WriteLine(str);
            loggingBox.Text += (str + "\n");
            loggingBox.CaretIndex = int.MaxValue; // Scroll to the bottom
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            findComponents();
        }

        private async void onLoad(object? sender, EventArgs args)
        {
            if(!firstActivation)
            {
                return;
            }
            firstActivation = false;

            welcomeText.Text += (" " + DebugBridge.APP_ID);

            // First install the debug bridge if it is missing
            try
            {
                await DebugBridge.InstallIfMissing();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + ": " + ex.StackTrace);
                return;
            }

            // Then we can check if the app is installed
            patchingPanel.IsVisible = true;

            bool appIsInstalled = DebugBridge.runCommand("shell pm list packages {app-id}") != "";
            if (appIsInstalled)
            {
                appInstalledText.IsVisible = true;
            }
            else
            {
                appNotInstalledText.IsVisible = true;
            }

            await moddingHandler.CheckInstallStatus();

            if (moddingHandler.AppInfo.IsModded)
            {
                await switchToModMenu();
            }
            else
            {
                loggingBox.Height = 195;
                startModding.IsVisible = true;
            }

            startModding.Click += onStartModdingClick;
            browseModsButton.Click += onBrowseForModsClick;
        }

        private async Task switchToModMenu() {
            this.MaxHeight += 200;
            this.MinHeight += 200;
            this.loggingBox.Height = 145;

            startModding.IsVisible = false;
            InstalledMods.IsVisible = true;
            
            await modsManager.LoadModsFromQuest();
        }

        private async void onStartModdingClick(object? sender, RoutedEventArgs args)
        {
            startModding.IsVisible = false;
            loggingBox.Height = 255;

            try
            {
                await moddingHandler.startModdingProcess();
            }   catch(Exception ex)
            {
                log("An error occurred while attempting to patch the game");
                log(ex.Message);
                Console.Error.WriteLine(ex.Message + ": " + ex.StackTrace);
                return;
            }

            await switchToModMenu();
        }

        private void onClose(object? sender, EventArgs args)
        {
            moddingHandler.RemoveTemporaryDirectory();
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
            try
            {
                ModInstallErrorText.IsVisible = false;
                await modsManager.InstallMod(files[0]);
            }   catch(Exception ex)
            {
                ModInstallErrorText.IsVisible = true;
                ModInstallErrorText.Text = "Error while installing mod: " + ex.Message;
                Console.Error.WriteLine(ex.Message + ": " + ex.StackTrace);
            }
        }

        private void findComponents()
        {
            appNotInstalledText = this.FindControl<TextBlock>("appNotInstalledText");
            appInstalledText = this.FindControl<TextBlock>("appInstalledText");
            loggingBox = this.FindControl<TextBox>("loggingBox");
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
