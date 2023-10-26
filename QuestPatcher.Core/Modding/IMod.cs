using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using QuestPatcher.Core.Models;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// A mod that has been loaded by QuestPatcher.
    /// This may be a QMOD, or another format.
    /// </summary>
    public interface IMod : INotifyPropertyChanged
    {
        /// <summary>
        /// Provider that loaded this mod.
        /// </summary>
        IModProvider Provider { get; }

        /// <summary>
        /// Unique ID of the mod, must not contain spaces.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Human readable name of the mod.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of the mod.
        /// </summary>
        string? Description { get; }

        /// <summary>
        /// Version of the mod.
        /// </summary>
        SemanticVersioning.Version Version { get; }

        /// <summary>
        /// Version of the package that the mod is intended for.
        /// </summary>
        string? PackageVersion { get; }

        /// <summary>
        /// Author of the mod.
        /// </summary>
        string Author { get; }

        /// <summary>
        /// Individual who ported this mod from another platform.
        /// </summary>
        string? Porter { get; }

        /// <summary>
        /// Keep going, keep going, keep going, keep going...
        /// </summary>
        string Robinson => "It will all be OK in the end";

        /// <summary>
        /// Whether or not the mod is currently installed.
        /// </summary>
        bool IsInstalled { get; }

        /// <summary>
        /// Whether or not the mod is a library.
        /// </summary>
        bool IsLibrary { get; }

        /// <summary>
        /// The file types that this mod supports.
        /// </summary>
        IEnumerable<FileCopyType> FileCopyTypes { get; }

        /// <summary>
        /// The modloader that this mod must be loaded with.
        /// </summary>
        ModLoader ModLoader { get; }

        /// <summary>
        /// Installs the mod.
        /// <exception cref="InstallationException">If installing the mod fails</exception>
        /// </summary>
        Task Install();

        /// <summary>
        /// Uninstalls the mod.
        /// <exception cref="InstallationException">If uninstalling the mod fails</exception>
        /// </summary>
        Task Uninstall();

        /// <summary>
        /// Opens the cover image for loading.
        /// </summary>
        /// <returns>A stream which can be used to load the cover image, or null if there is no cover image.</returns>
        Task<Stream?> OpenCover();
    }
}
