using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuestPatcher.Core.Models
{
    public class ApkInfo : INotifyPropertyChanged
    {
        public string Version { get;  }
        public bool IsModded {
            get => _isModded;
            set
            {
                if(_isModded != value)
                {
                    _isModded = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool Is64Bit { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isModded;
        
        public ApkInfo(string version, bool isModded, bool is64Bit)
        {
            Version = version;
            _isModded = isModded;
            Is64Bit = is64Bit;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
