using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Data;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.Services
{
    /// <summary>
    /// Manages loading themes and the currently selected theme
    /// </summary>
    public class ThemeManager : ReactiveObject
    {
        private const string ThemesDirectoryName = "themes";

        public Theme SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (_selectedTheme != value)
                {
                    _selectedTheme = value;
                    this.RaisePropertyChanged();

                    // Update in the config so that the selected theme persists
                    _config.SelectedThemeName = value.Name;
                    UpdateThemeStyling();
                }
            }
        }
        private Theme _selectedTheme;

        public string ThemesDirectory { get; }

        public List<Theme> AvailableThemes { get; } = new();

        private readonly Config _config;

        public ThemeManager(Config config, SpecialFolders specialFolders)
        {
            _config = config;

            ThemesDirectory = Path.Combine(specialFolders.DataFolder, ThemesDirectoryName);
            Directory.CreateDirectory(ThemesDirectory);

            AddDefaultThemes();
            LoadCustomThemes();
            Log.Debug("{AvailableThemes} themes loaded successfully!", AvailableThemes.Count);

            // Default back to the dark theme if the selected theme was deleted
            _selectedTheme = AvailableThemes.FirstOrDefault(theme => theme.Name == config.SelectedThemeName) ?? AvailableThemes.Single(theme => theme.Name == "Dark");
            UpdateThemeStyling(true);
        }

        private void AddDefaultThemes()
        {
            Log.Debug("Loading default themes");
            AvailableThemes.Add(Theme.LoadEmbeddedTheme("Styles/Themes/QuestPatcherDark.axaml", "Dark"));
            AvailableThemes.Add(Theme.LoadEmbeddedTheme("Styles/Themes/QuestPatcherLight.axaml", "Light"));
        }

        private void LoadCustomThemes()
        {
            // Make sure that necessary assemblies are loaded first
            var _ = typeof(TemplateBinding);

            foreach (string themeDirName in Directory.EnumerateDirectories(ThemesDirectory))
            {
                Log.Debug("Loading theme from {ThemeDirectoryName}", themeDirName);
                try
                {
                    AvailableThemes.Add(Theme.LoadFromDirectory(themeDirName));
                }
                catch (Exception ex)
                {
                    // TODO: Show an exception dialog instead of just logging?
                    Log.Error(ex, "Failed to load theme from {ThemeDirectoryName}", themeDirName);
                }
            }
        }

        /// <summary>
        /// Updates the current theme in the styles of the open Avalonia application
        /// </summary>
        /// <param name="init">Whether or not this is the theme being used during startup</param>
        /// <exception cref="InvalidOperationException">If attempting to update the current theme when no application is open.</exception>
        private void UpdateThemeStyling(bool init = false)
        {
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Cannot update theme styling when no app is open");
            }

            if (init)
            {
                Application.Current.Styles.Insert(0, _selectedTheme.ThemeStying);
            }
            else
            {
                Application.Current.Styles[0] = _selectedTheme.ThemeStying;
            }
        }
    }
}
