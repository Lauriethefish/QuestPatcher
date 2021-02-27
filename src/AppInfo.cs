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

        public AppInfo(string apkPath, string decompilationPath)
        {
            // Unfortunately apktool doesn't extract the tag file, so we manually open the APK
            ZipArchive apkArchive = ZipFile.OpenRead(apkPath);
            IsModded = apkArchive.GetEntry("modded") != null;
            apkArchive.Dispose();

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(decompilationPath + "AndroidManifest.xml");
            // Unable to find the version in the manifest atm

        }
    }
}
