using System.Collections.Generic;
using Avalonia.Platform.Storage;

namespace QuestPatcher
{
    internal static class FilePickerTypes
    {
        public static readonly FilePickerFileType QMod = new("Quest Mods")
        {
            Patterns = new List<string>() { "*.qmod" }
        };

        public static readonly FilePickerFileType ZipFile = new("Zip File")
        {
            Patterns = new List<string>() { "*.zip" }
        };
    }
}
