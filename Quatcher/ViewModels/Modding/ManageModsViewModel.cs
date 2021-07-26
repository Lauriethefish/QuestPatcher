using Avalonia.Controls;
using Quatcher.Core.Modding;
using Quatcher.Core.Patching;
using Quatcher.Models;

namespace Quatcher.ViewModels.Modding
{
    public class ManageModsViewModel : ViewModelBase
    {
        public ModListViewModel ModsList { get; }

        public ModListViewModel LibrariesList { get; }

        public ProgressViewModel ProgressView { get; }

        public ManageModsViewModel(ModManager modManager, PatchingManager patchingManager, Window mainWindow, OperationLocker locker, ProgressViewModel progressView, BrowseImportManager browseManager)
        {
            ProgressView = progressView;
            ModsList = new ModListViewModel("Mods", true, modManager.Mods, modManager, patchingManager, mainWindow, locker, browseManager);
            LibrariesList = new ModListViewModel("Libraries", false, modManager.Libraries, modManager, patchingManager, mainWindow, locker, browseManager);
        }
    }
}
