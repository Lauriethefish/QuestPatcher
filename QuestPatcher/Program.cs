using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.Threading;
using System.Threading.Tasks;
using CliFx;

namespace QuestPatcher
{
    public class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static async Task<int> Main(string[] args)
        {
            if(args.Length > 0)
            {
                return await new CliApplicationBuilder()
                    .AddCommandsFromThisAssembly()
                    .SetExecutableName("QuestPatcher")
                    .Build()
                    .RunAsync();
            }
            else
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        private static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}
