using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DryIoc;
using QuestPatcher.Core;
using QuestPatcher.Models;
using QuestPatcher.ViewModels;
using QuestPatcher.ViewModels.Modding;
using QuestPatcher.Views;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Container = DryIoc.Container;

namespace QuestPatcher
{
    public class App : Application
    {
        private const int WindowWidth = 900;
        private const int WindowHeight = 550;
        
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
                    ContinueInit(desktop);
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

        private void RegisterModels(Container container)
        {
            container.Register<LoadedViewModel>();
            container.Register<LoadingViewModel>();
            container.Register<MainWindowViewModel>();
            container.Register<OtherItemsViewModel>();
            container.Register<PatchingViewModel>();
            container.Register<ProgressViewModel>();
            container.Register<ToolsViewModel>();
            container.Register<ManageModsViewModel>();
            container.Register<UIManager>(Reuse.Singleton);
            container.Register<BrowseImportManager>(Reuse.Singleton);
            container.Register<OperationLocker>(Reuse.Singleton);
            container.Register<LoggingViewModel>(Reuse.Singleton);
        }
        
        private Logger SetupLogging(SpecialFolders specialFolders, TextBoxSink textBoxSink)
        {
            string logsPath = System.IO.Path.Combine(specialFolders.LogsFolder, "log.log");
            
            LoggerConfiguration configuration = new();
            configuration.MinimumLevel.Verbose()
                .WriteTo.File(logsPath, LogEventLevel.Verbose,
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Console()
                .WriteTo.Sink(textBoxSink, LogEventLevel.Information);
            // TODO: Add back writing to QuestPatcher logs window

            return configuration.CreateLogger();
        }

        private void ContinueInit(IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Prepares QuestPatcher application folders, e.g. the logs folder
            SpecialFolders specialFolders = SpecialFolders.SetupStandardFolders();

            TextBoxSink textBoxSink = new();
            // Now we need to setup logging
            Logger logger = SetupLogging(specialFolders, textBoxSink);

            var container = Bootstrap.RegisterQuestPatcherServices<UIPrompter>(specialFolders, logger);
            RegisterModels(container);

            MainWindow mainWindow = new()
            {
                Width = WindowWidth,
                Height = WindowHeight,
            };
            container.UseInstance(mainWindow);
            container.UseInstance(textBoxSink);
            container.UseInstance<IControlledApplicationLifetime>(desktop);
            
            // Calls the QuestPatcherService constructor which starts everything up (early init)
            container.Resolve<UIManager>();
            desktop.MainWindow = mainWindow;

        }
    }
}
