using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuestPatcher.Core;
using QuestPatcher.Services;

namespace QuestPatcher
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
                QuestPatcherService questPatcherService = new QuestPatcherUIService(desktop);
                desktop.Exit += (sender, args) =>
                {
                    questPatcherService.CleanUp();
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
