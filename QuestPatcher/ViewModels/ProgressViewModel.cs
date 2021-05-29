using QuestPatcher.Core;
using QuestPatcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
