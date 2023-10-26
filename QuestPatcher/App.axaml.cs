using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuestPatcher.Models;
using QuestPatcher.Services;
using QuestPatcher.Views;
using Serilog;

namespace QuestPatcher
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs args)
        {
            if (!args.IsTerminating)
            {
                return;
            }

            Log.Error((Exception) args.ExceptionObject, "Unhandled exception, QuestPatcher quitting!");
            Log.CloseAndFlush();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

                try
                {
                    var questPatcherService = new QuestPatcherUiService(desktop);
                    desktop.Exit += (_, _) =>
                    {
                        questPatcherService.CleanUp();
                    };
                }
                catch (Exception ex)
                {
                    // Load the default dark theme if we crashed so early in startup that themes hadn't yet been loaded
                    if (Styles.Count == 1)
                    {
                        Styles.Insert(0,
                            Theme.LoadEmbeddedTheme("Styles/Themes/QuestPatcherDark.axaml", "Dark").ThemeStying);
                    }

                    DialogBuilder dialog = new()
                    {
                        Title = "Critical Error",
                        Text = "QuestPatcher encountered a critical error during early startup, which was unrecoverable.",
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
