using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;

namespace Quatcher
{
    public class App : Application
    {
        private IClassicDesktopStyleApplicationLifetime desktop;
        private MainWindow mainWindow;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            desktop = (IClassicDesktopStyleApplicationLifetime) ApplicationLifetime;

            mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            base.OnFrameworkInitializationCompleted();
        }
    }
}
