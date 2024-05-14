using QuestPatcher.Axml;

namespace ACVPatcher
{
    public class AxmlManager
    {

        public const int NameAttributeResourceId = 16842755;
        public const int ExportedAttributeResourceId = 16842768;
        public const int TargetPackageResrouceId = 16842785;
        public const int EnabledAttributeResourceId = 16842766;
        public static Uri AndroidNamespaceUri = new("http://schemas.android.com/apk/res/android");
        public static ISet<string> GetExistingChildren(AxmlElement manifest, string childNames)
        {
            HashSet<string> result = new();

            foreach (var element in manifest.Children)
            {
                if (element.Name != childNames) { continue; }

                var nameAttributes = element.Attributes.Where(attribute => attribute.Namespace == AndroidNamespaceUri && attribute.Name == "name").ToList();
                // Only add children with the name attribute
                if (nameAttributes.Count > 0) { result.Add((string) nameAttributes[0].Value); }
            }

            return result;
        }

        public static void AddNameAttribute(AxmlElement element, string name)
        {
            element.Attributes.Add(new AxmlAttribute("name", AndroidNamespaceUri, NameAttributeResourceId, name));
        }

        public static void AddExportedAttribute(AxmlElement element, bool exported)
        {
            element.Attributes.Add(new AxmlAttribute("exported", AndroidNamespaceUri, ExportedAttributeResourceId, exported ? "true" : "false"));
        }

        public static void AddTargetPackageAttribute(AxmlElement element, string targetPackage)
        {
            element.Attributes.Add(new AxmlAttribute("targetPackage", AndroidNamespaceUri, TargetPackageResrouceId, targetPackage));
        }

        public static void AddEnabledAttribute(AxmlElement element, bool enabled)
        {
            element.Attributes.Add(new AxmlAttribute("enabled", AndroidNamespaceUri, EnabledAttributeResourceId, enabled ? "true" : "false"));
        }

        internal static string GetPackage(AxmlElement manifest)
        {
            return manifest.Attributes.Single(attr => attr.Name == "package")?.Value as string ?? string.Empty;
        }

    }
}