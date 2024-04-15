using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Modding;
using QuestPatcher.Models;
using QuestPatcher.Resources;

namespace QuestPatcher.ViewModels.Modding
{
    public class ManageModsViewModel : ViewModelBase
    {
        public ModListViewModel ModsList { get; }

        public ModListViewModel LibrariesList { get; }

        public ProgressViewModel ProgressView { get; }

        public ManageModsViewModel(ModManager modManager, InstallManager installManager, Window mainWindow, OperationLocker locker, ProgressViewModel progressView, BrowseImportManager browseManager)
        {
            ProgressView = progressView;
            ModsList = new ModListViewModel(Strings.Mod_Section_Mods, true, modManager.Mods, modManager, installManager, mainWindow, locker, browseManager);
            LibrariesList = new ModListViewModel(Strings.Mod_Section_Libraries, false, modManager.Libraries, modManager, installManager, mainWindow, locker, browseManager);
        }
    }
}
