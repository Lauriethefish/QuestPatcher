using Avalonia.Controls;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Patching;
using QuestPatcher.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.ViewModels.Modding
{
    public class ManageModsViewModel : ViewModelBase
    {
        public ModListViewModel ModsList { get; }

        public ModListViewModel LibrariesList { get; }

        public OperationLocker Locker { get; }

        public ManageModsViewModel(ModManager modManager, PatchingManager patchingManager, Window mainWindow, OperationLocker locker, BrowseImportManager browseManager)
        {
            Locker = locker;
            ModsList = new ModListViewModel("Mods", true, modManager.Mods, modManager, patchingManager, mainWindow, locker, browseManager);
            LibrariesList = new ModListViewModel("Libraries", false, modManager.Libraries, modManager, patchingManager, mainWindow, locker, browseManager);
        }
    }
}
