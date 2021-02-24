using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Threading.Tasks;

namespace QuestPatcher
{
    public class MainWindow : Window
    {
        private TextBlock appNotInstalledText;
        private TextBlock appInstalledText;
        private TextBox loggingBox;
        private Button startModding;

        public DebugBridge DebugBridge { get; } = new DebugBridge();
        private ModdingHandler moddingHandler;

        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif      
            this.moddingHandler = new ModdingHandler(this);
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
        }

        private async void onStartModdingClick(object sender, RoutedEventArgs args)
        {
            startModding.IsVisible = false;
            loggingBox.Height += 60;

            try
            {
                await moddingHandler.startModdingProcess();
            }   catch(Exception ex)
            {
                log("An error occurred while attempting to patch the game");
                log(ex.Message);
            }
        }

        private void findComponents()
        {
            appNotInstalledText = this.FindControl<TextBlock>("appNotInstalledText");
            appInstalledText = this.FindControl<TextBlock>("appInstalledText");
            loggingBox = this.FindControl<TextBox>("loggingBox");
            startModding = this.FindControl<Button>("startModding");
        }
    }
}
