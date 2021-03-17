using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
using System.Xml;

namespace QuestPatcher
{
    public class AppInfo
    {
        public bool IsModded { get; }
        public string GameVersion { get; }

        public AppInfo(bool isModded, string gameVersion)
        {
            this.IsModded = isModded;
            this.GameVersion = gameVersion;
        }
    }
}
