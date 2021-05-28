using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;

namespace QuestPatcher.ViewModels
{
    public class LoggingViewModel : ViewModelBase
    {
        public string LoggedText
        {
            get
            {
                return _loggedText;
            }
            set
            {
                this.RaiseAndSetIfChanged(ref _loggedText, value);
            }
        }

        private string _loggedText = "";
    }
}
