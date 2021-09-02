using DryIoc;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using Serilog.Core;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Convenient class for QuestPatcher init
    /// </summary>
    public static class Bootstrap
    {
        /// <summary>
        /// Starts up <see cref="QuestPatcherService"/> with the given parameters.
        /// Uses a DryIoc container to inject dependencies of the service.
        /// </summary>
        /// <param name="specialFolders">Folders to store QuestPatcher files in</param>
        /// <param name="logger">Logger used for all QuestPatcher logging</param>
        /// <typeparam name="T">Implementation of <see cref="ICallbacks"/> to use</typeparam>
        /// <returns>The container used to load QuestPatcher, and the service itself</returns>
        public static Container RegisterQuestPatcherServices<T>(SpecialFolders specialFolders, Logger logger) where T: ICallbacks
        {
            ConfigManager configManager = new(logger, specialFolders);
            Config config = configManager.GetOrLoadConfig();
            
            // Resolve all the dependencies using dryioc
            Container container = new();
            container.UseInstance(specialFolders);
            container.UseInstance(logger);
            container.UseInstance(config);
            container.UseInstance(configManager);
            
            container.Register<ICallbacks, T>(Reuse.Singleton);
            container.Register<ApkTools>(Reuse.Singleton);
            container.Register<AndroidDebugBridge>(Reuse.Singleton);
            container.Register<InfoDumper>(Reuse.Singleton);
            container.Register<ExternalFilesDownloader>(Reuse.Singleton);
            container.Register<QuestPatcherService>(Reuse.Singleton);

            container.Register<ModManager>(Reuse.ScopedOrSingleton);
            container.Register<OtherFilesManager>(Reuse.ScopedOrSingleton);
            container.Register<PatchingManager>(Reuse.ScopedOrSingleton);
            return container;
        }
    }
}