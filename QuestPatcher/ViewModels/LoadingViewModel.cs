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
        public LoggingViewModel LoggingView { get; set; }

        public Config Config { get; }

        public LoadingViewModel(LoggingViewModel loggingView, Config config)
        {
            LoggingView = loggingView;
            Config = config;
        }
    }
}
