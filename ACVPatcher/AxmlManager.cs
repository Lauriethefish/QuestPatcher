using QuestPatcher.Axml;

namespace ACVPatcher
{
    public class AxmlManager
    {

        public const int NameAttributeResourceId = 16842755;
        public const int ExportedAttributeResourceId = 16842768;
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
    }
}