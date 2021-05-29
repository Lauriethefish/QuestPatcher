using QuestPatcher.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using QuestPatcher.Services;
using QuestPatcher.Core.Models;

namespace QuestPatcher.ViewModels
{
    public class LoadingViewModel : ViewModelBase
    {
        public ProgressViewModel ProgressView { get; }
        public LoggingViewModel LoggingView { get; }

        public Config Config { get; }

        public LoadingViewModel(ProgressViewModel progressView, LoggingViewModel loggingView, Config config)
        {
            ProgressView = progressView;
            LoggingView = loggingView;
            Config = config;
        }
    }
}
