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
        private Button alreadyModded;
        public TextBlock ModInstallErrorText { get; private set; }

        private Button browseModsButton;
        private Panel patchingPanel;
        public StackPanel InstalledModsPanel { get; private set; }

        public DebugBridge DebugBridge { get; } = new DebugBridge();
        private ModdingHandler moddingHandler;

        private ModsManager modsManager;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif      
            this.moddingHandler = new ModdingHandler(this);
            this.modsManager = new ModsManager(this);
        }

        // Writes a new line to the "modding log" section
        public void log(string str)
        {
            loggingBox.Text += (str + "\n");
            loggingBox.CaretIndex = int.MaxValue; // Scroll to the bottom
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            findComponents();

            welcomeText.Text += (" " + DebugBridge.APP_ID);

            bool appIsInstalled = DebugBridge.runCommand("shell pm list packages {app-id}") != "";
            if(appIsInstalled)
            {
                appInstalledText.IsVisible = true;
            }
            else
            {
                appNotInstalledText.IsVisible = true;
                startModding.IsVisible = false; // Remove the "mod the game" button
            }

            startModding.Click += onStartModdingClick;
            alreadyModded.Click += onAlreadyModdedClick;
            browseModsButton.Click += onBrowseForModsClick;
        }

        private async Task switchToModMenu() {
            patchingPanel.IsVisible = false;
            startModding.IsVisible = false;
            alreadyModded.IsVisible = false;
            InstalledModsPanel.IsVisible = true;
            
            await modsManager.LoadModsFromQuest();
        }

        private async void onStartModdingClick(object? sender, RoutedEventArgs args)
        {
            startModding.IsVisible = false;
            alreadyModded.IsVisible = false;
            loggingBox.Height += 60;

            try
            {
                await moddingHandler.startModdingProcess();
            }   catch(Exception ex)
            {
                log("An error occurred while attempting to patch the game");
                log(ex.Message);
                return;
            }

            await switchToModMenu();
        }

        private async void onAlreadyModdedClick(object? sender, RoutedEventArgs args) {
            await switchToModMenu();
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

            // Install the mod with that path
            try
            {
                await modsManager.InstallMod(files[0]);
            }   catch(Exception ex)
            {
                ModInstallErrorText.Text = "Error while installing mod: " + ex.Message;
            }
        }

        private void findComponents()
        {
            appNotInstalledText = this.FindControl<TextBlock>("appNotInstalledText");
            appInstalledText = this.FindControl<TextBlock>("appInstalledText");
            loggingBox = this.FindControl<TextBox>("loggingBox");
            startModding = this.FindControl<Button>("startModding");
            alreadyModded = this.FindControl<Button>("alreadyModded");
            welcomeText = this.FindControl<TextBlock>("welcomeText");
            patchingPanel = this.FindControl<Panel>("patchingPanel");
            InstalledModsPanel = this.FindControl<StackPanel>("installedMods");
            browseModsButton = this.FindControl<Button>("browseModsButton");
            ModInstallErrorText = this.FindControl<TextBlock>("modInstallErrorText");
        }
    }
}
