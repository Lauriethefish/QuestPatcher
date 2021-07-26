using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Quatcher.Core;
using Quatcher.Services;
using Quatcher.Views;

namespace Quatcher
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    QuatcherService questPatcherService = new QuatcherUIService(desktop);
                    
                    desktop.Exit += (_, _) =>
                    {
                        questPatcherService.CleanUp();
                    };
                }
                catch (Exception ex)
                {
                    DialogBuilder dialog = new()
                    {
                        Title = "Critical Error",
                        Text = "Quatcher encountered a critical error during early startup, which was unrecoverable.",
                        HideCancelButton = true
                    };
                    dialog.WithException(ex);
                    dialog.OpenDialogue(null, WindowStartupLocation.CenterScreen);
                }
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
