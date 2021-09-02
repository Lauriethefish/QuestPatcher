using ReactiveUI;

namespace QuestPatcher.ViewModels
{
    public class LoggingViewModel : ViewModelBase
    {
        public string LoggedText
        {
            get => _loggedText;
            set
            {
                _loggedText = value;
                this.RaisePropertyChanged();
            }
        }

        private string _loggedText = "";

        public LoggingViewModel(TextBoxSink textBoxSink)
        {
            textBoxSink.Init(AddLine);
        }

        private void AddLine(string line)
        {
            LoggedText += $"{line}\n";
        }
    }
}
