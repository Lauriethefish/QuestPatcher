using System.Globalization;
using QuestPatcher.Core.Models;

namespace QuestPatcher
{
    public static class LanguageEnumExtensions
    {
        public static CultureInfo ToCultureInfo(this Language language)
        {
            return language switch
            {
                Language.English => new CultureInfo("en-US"),
                Language.ChineseSimplified => new CultureInfo("zh-hans"),
                _ => CultureInfo.InstalledUICulture,
            };
        }
    }
}
