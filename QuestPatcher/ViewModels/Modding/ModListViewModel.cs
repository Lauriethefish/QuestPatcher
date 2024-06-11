using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Modding;
using QuestPatcher.Models;
using QuestPatcher.Views;

namespace QuestPatcher.ViewModels.Modding
{
    public class ModListViewModel : ViewModelBase
    {
        public string Title { get; }

        public bool ShowBrowse { get; }

        public OperationLocker Locker { get; }
        public ObservableCollection<ModViewModel> DisplayedMods { get; } = new();

        private readonly BrowseImportManager _browseManager;
        private readonly ModManager _modManager;
        private readonly MainWindow? _mainWindow;
        private readonly InstallManager _installManager;

        public ModListViewModel(string title, bool showBrowse, ObservableCollection<IMod> mods, ModManager modManager, InstallManager installManager, Window mainWindow, OperationLocker locker, BrowseImportManager browseManager)
        {
            Title = title;
            ShowBrowse = showBrowse;
            Locker = locker;
            _browseManager = browseManager;
            _modManager = modManager;
            _mainWindow = mainWindow as MainWindow;
            _installManager = installManager;

            // There's probably a better way to create my ModViewModel for the mods in this ObservableCollection
            // If there if, please tell me/PR it.
            // I can't just use the mods directly because I want to add prompts for installing/uninstalling (e.g. incorrect game version)
            mods.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    DisplayedMods.Clear();
                    return;
                }

                if (args.NewItems != null)
                {
                    foreach (IMod mod in args.NewItems)
                    {
                        DisplayedMods.Add(new ModViewModel(mod, modManager, installManager, mainWindow, locker));
                    }
                }
                if (args.OldItems != null)
                {
                    foreach (IMod mod in args.OldItems)
                    {
                        DisplayedMods.Remove(DisplayedMods.Single(modView => modView.Mod == mod));
                    }
                }
            };
        }

        public async void OnBackupClick()
        {
            if (_mainWindow == null)
            {
                return;
            }

            Locker.StartOperation();
            try
            {
                var app = _installManager.InstalledApp;

                if (app == null)
                {
                    return;
                }

                var outFilename = await _mainWindow.StorageProvider.SaveFilePickerAsync(new()
                {
                    FileTypeChoices = new[] { FilePickerTypes.ZipFile },
                    SuggestedFileName = $"{_modManager.AppId}_{app.Version}_{DateTime.Now.ToString("yyyyMMddTHHmmss")}.zip"
                });

                if (outFilename != null)
                {
                    using (var file = File.Create(outFilename.Path.LocalPath))
                    using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                    {
                        foreach (var mod in _modManager.Mods.Concat(_modManager.Libraries).OrderBy(mod => mod.Id))
                        {
                            var entry = zip.CreateEntry($"{mod.Id}.qmod");

                            using (var modStream = entry.Open())
                            {
                                await _modManager.BackupMod(mod, modStream);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public async void OnBrowseClick()
        {
            await _browseManager.ShowModsBrowse();
        }
    }
}
