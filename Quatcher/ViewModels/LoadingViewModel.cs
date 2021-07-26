﻿using Quatcher.Core.Models;

namespace Quatcher.ViewModels
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
