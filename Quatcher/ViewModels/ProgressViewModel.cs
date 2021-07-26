using Quatcher.Core;
using Quatcher.Models;

namespace Quatcher.ViewModels
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
