using QuestPatcher.Core;
using QuestPatcher.Models;

namespace QuestPatcher.ViewModels
{
    public class ProgressViewModel : ViewModelBase
    {
        public OperationLocker Locker { get; }
        public ExternalFilesDownloader FilesDownloader { get; }

        public ProgressViewModel(OperationLocker locker, ExternalFilesDownloader filesDownloader)
        {
            Locker = locker;
            FilesDownloader = filesDownloader;
        }
    }
}
