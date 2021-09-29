using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using QuestPatcher.Core;
using QuestPatcher.Services;
using QuestPatcher.ViewModels;
using QuestPatcher.Views;

namespace QuestPatcher
{
    public class App : Application
    {
        private static readonly IStyle LightTheme = new StyleInclude(new Uri("resm:Styles?assembly=QuestPatcher")) 
        { 
            Source = new Uri("avares://QuestPatcher/Styles/Themes/QuestPatcherLight.axaml") 
        }; 
        private static readonly IStyle DarkTheme = new StyleInclude(new Uri("resm:Styles?assembly=QuestPatcher")) 
        { 
            Source = new Uri("avares://QuestPatcher/Styles/Themes/QuestPatcherDark.axaml") 
        };

        private static bool _alreadySetTheme;
        
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public static void SetTheme(bool light)
        {
            IStyle newTheme = light ? LightTheme : DarkTheme;
            if(_alreadySetTheme)
            {
                Current.Styles[0] = newTheme;
            }
            else
            {
                Current.Styles.Insert(0, newTheme);
                _alreadySetTheme = true;
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    QuestPatcherService questPatcherService = new QuestPatcherUIService(desktop);
                    
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
