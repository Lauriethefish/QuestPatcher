using ReactiveUI;

namespace QuestPatcher.ViewModels
{
    public class LoggingViewModel : ViewModelBase
    {
        public string LoggedText
        {
            get => _loggedText;
            set => this.RaiseAndSetIfChanged(ref _loggedText, value);
        }

        private string _loggedText = "";
    }
}
