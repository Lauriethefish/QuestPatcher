using System;
using System.IO;
using System.Linq;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace QuestPatcher.Models
{
    public class Theme
    {
        private const string ThemeStylesPath = "Styling.axaml";

        /// <summary>
        /// Styling to use for this theme
        /// </summary>
        public IStyle ThemeStying
        {
            get
            {
                // Lazily load the style for custom themes, to avoid loading everything at startup
                return _loadedStyle ??= LoadStylesFrom(_xamlFilePath!);
            }
        }

        private IStyle? _loadedStyle;
        private readonly string? _xamlFilePath;

        /// <summary>
        /// Name of the theme
        /// </summary>
        public string Name { get; }

        private Theme(string xamlFilePath, string name)
        {
            _xamlFilePath = xamlFilePath;
            Name = name;
        }

        private Theme(IStyle nonLazyStyle, string name)
        {
            _loadedStyle = nonLazyStyle;
            Name = name;
        }

        private Styles LoadStylesFrom(string xamlPath)
        {
            string styleXaml = File.ReadAllText(xamlPath);
            return AvaloniaRuntimeXamlLoader.Parse<Styles>(styleXaml);
        }

        /// <summary>
        /// Loads a theme from the given directory path.
        /// </summary>
        /// <param name="path">Path to load the theme from</param>
        /// <returns>Loaded theme</returns>
        public static Theme LoadFromDirectory(string path)
        {
            return new(Path.Combine(path, ThemeStylesPath), path.Split(Path.DirectorySeparatorChar).Last());
        }

        /// <summary>
        /// Loads a theme from the given XAML path within the QuestPatcher project.
        /// </summary>
        /// <param name="xamlPath">Path to the XAML for the theme</param>
        /// <param name="name">Name of the theme</param>
        /// <returns>Loaded theme</returns>
        public static Theme LoadEmbeddedTheme(string xamlPath, string name)
        {
            IStyle styling = new StyleInclude(new Uri("resm:Styles?assembly=QuestPatcher"))
            {
                Source = new Uri($"avares://QuestPatcher/{xamlPath}")
            };

            return new Theme(styling, name);
        }
    }
}
