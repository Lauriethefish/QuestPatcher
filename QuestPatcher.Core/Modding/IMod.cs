using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    public interface IMod : INotifyPropertyChanged
    {
        /// <summary>
        /// Provider that loaded this mod
        /// </summary>
        IModProvider Provider { get; }
        
        /// <summary>
        /// Unique ID of the mod, must not contain spaces
        /// </summary>
        string Id { get; }
        
        /// <summary>
        /// Human readable name of the mod
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Description of the mod
        /// </summary>
        string? Description { get; }

        /// <summary>
        /// Version of the mod
        /// </summary>
        SemanticVersioning.Version Version { get; }
        
        /// <summary>
        /// Version of the package that the mod is intended for
        /// </summary>
        string PackageVersion { get; }
        
        /// <summary>
        /// Author of the mod
        /// </summary>
        string Author { get; }
        
        /// <summary>
        /// Individual who ported this mod from another platform
        /// </summary>
        string? Porter { get; }

        /// <summary>
        /// Keep going, keep going, keep going, keep going
        /// </summary>
        string Robinson => "It will all be OK in the end";
        
        /// <summary>
        /// Whether or not the mod is currently installed
        /// </summary>
        bool IsInstalled { get; }
        
        /// <summary>
        /// Whether or not the mod is a library
        /// </summary>
        bool IsLibrary { get; }

        /// <summary>
        /// Installs the mod
        /// </summary>
        /// <returns>Task that will complete once the mod is installed</returns>
        Task Install();

        /// <summary>
        /// Uninstalls the mod
        /// </summary>
        /// <returns>Task that will complete once the mod is uninstalled</returns>
        Task Uninstall();

        /// <summary>
        /// Opens the cover image for loading.
        /// </summary>
        /// <returns>A stream which can be used to load the cover image, or null if there is no cover image</returns>
        Task<Stream?> OpenCover();
    }
}
