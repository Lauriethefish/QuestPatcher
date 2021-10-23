using System.Threading.Tasks;
using CliFx;
using CliFx.Infrastructure;
using QuestPatcher.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace QuestPatcher.CLI
{
    public abstract class QuestPatcherCommand : ICommand
    {
        protected SpecialFolders SpecialFolders { get; }
        
        protected ExternalFilesDownloader FilesDownloader { get; }
        
        protected Logger Logger { get; }
        
        public QuestPatcherCommand()
        {
            SpecialFolders = new SpecialFolders();
            Logger = new LoggerConfiguration()
                .WriteTo.Console(LogEventLevel.Information, "{Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            FilesDownloader = new ExternalFilesDownloader(SpecialFolders, Logger);
        }
        
        public abstract ValueTask ExecuteAsync(IConsole console);
    }
}
