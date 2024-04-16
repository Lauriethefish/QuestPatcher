using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Models
{
    public class Config : INotifyPropertyChanged
    {
        private string _appId = "";
        public string AppId
        {
            get => _appId;
            set
            {
                if (value != _appId)
                {
                    _appId = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [DefaultValue(Language.Default)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Language Language { get; set; } = Language.Default;

        private bool _displayLogs;

        [DefaultValue(false)]
        public bool DisplayLogs
        {
            get => _displayLogs;
            set
            {
                if (value != _displayLogs)
                {
                    _displayLogs = value;
                    NotifyPropertyChanged();
                }
            }
        }

        [DefaultValue(null)]
        [JsonPropertyName("patchingPermissions")]
        public PatchingOptions PatchingOptions
        {
            get => _patchingPermissions;
            set
            {
                // Used to get round default JSON values not being able to be objects. We instead set it to null by default then have the default backing field set to the default value
                if (value != _patchingPermissions && value != null)
                {
                    _patchingPermissions = value;
                    NotifyPropertyChanged();
                }

            }
        }
        private PatchingOptions _patchingPermissions = new();

        [DefaultValue(false)]
        public bool ShowPatchingOptions
        {
            get => _showPatchingOptions;
            set
            {
                if (value != _showPatchingOptions)
                {
                    _showPatchingOptions = value;
                    NotifyPropertyChanged();
                }
            }
        }
        private bool _showPatchingOptions;

        public string SelectedThemeName { get; set; } = "Dark";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
