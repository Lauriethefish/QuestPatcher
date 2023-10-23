using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuestPatcher.Core.Models
{
    public class ApkInfo : INotifyPropertyChanged
    {
        /// <summary>
        /// The version of the APK
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Whether or not the APK is modded with a modloader that we recognise (QuestLoader or Scotland2)
        /// </summary>
        public bool IsModded => ModLoader != null && ModLoader != Modloader.Unknown;

        /// <summary>
        /// The modloader that the APK is modded with.
        /// Null if unmodded.
        /// </summary>
        public Modloader? ModLoader
        {
            get => _modloader;
            set
            {
                if (_modloader != value)
                {
                    _modloader = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(IsModded));
                }
            }
        }

        /// <summary>
        /// Whether or not the APK uses 64 bit binary files.
        /// </summary>
        public bool Is64Bit { get; }

        /// <summary>
        /// The path of the local APK, downloaded from the quest
        /// </summary>
        public string Path { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private Modloader? _modloader;

        public ApkInfo(string version, Modloader? modloader, bool is64Bit, string path)
        {
            Version = version;
            _modloader = modloader;
            Is64Bit = is64Bit;
            Path = path;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
