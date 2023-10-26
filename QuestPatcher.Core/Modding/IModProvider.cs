using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// A manager for a particular type of mod.
    /// Allows QuestPatcher to support multiple mod types.
    /// </summary>
    public interface IModProvider
    {
        /// <summary>
        /// File extension of mod files that can be loaded by this provider, lowercase, no period prefix.
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Loads a mod from the given path and copies the files to the quest if necessary.
        /// Whenever a mod is loaded, by this method or by dependency installation, it should call <see cref="ModManager.ModLoadedCallback"/>.
        /// </summary>
        /// <param name="modPath">Path of the mod file.</param>
        /// <exception cref="InstallationException">If loading the mod failed.</exception>
        /// <returns>The loaded mod.</returns>
        Task<IMod> LoadFromFile(string modPath);

        /// <summary>
        /// Deletes the given mod from the quest and removes it from this provider.
        /// Whenever a mod is loaded, by this method or by dependency installation, it should call <see cref="ModManager.ModRemovedCallback"/>.
        /// </summary>
        /// <param name="mod">Mod to delete.</param>
        /// <exception cref="ArgumentException">If the given mod was not a mod loaded with this provider.</exception>
        /// <exception cref="InstallationException">If uninstalling the mod prior to removal fails.</exception>
        Task DeleteMod(IMod mod);

        /// <summary>
        /// Loads the mods from the quest.
        /// </summary>
        Task LoadModsStatus();

        /// <summary>
        /// Clears currently loaded mods.
        /// </summary>
        void ClearMods();

        /// <summary>
        /// Invoked if no mod config is found when loading mods.
        /// Attempts to load mods in an outdated format.
        /// </summary>
        Task LoadLegacyMods();
    }
}
