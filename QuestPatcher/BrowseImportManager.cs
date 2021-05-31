using Avalonia.Controls;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Patching;
using QuestPatcher.Models;
using QuestPatcher.Views;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuestPatcher
{
    /// <summary>
    /// Handles creating browse dialogs for importing files, and also the importing of unknown files
    /// </summary>
    public class BrowseImportManager
    {
        private readonly OtherFilesManager _otherFilesManager;
        private readonly ModManager _modManager;
        private readonly Window _mainWindow;
        private readonly Logger _logger;
        private readonly PatchingManager _patchingManager;
        private readonly OperationLocker _locker;

        private readonly FileDialogFilter _modsFilter = new();

        private Queue<string>? _currentImportQueue;

        public BrowseImportManager(OtherFilesManager otherFilesManager, ModManager modManager, Window mainWindow, Logger logger, PatchingManager patchingManager, OperationLocker locker)
        {
            _otherFilesManager = otherFilesManager;
            _modManager = modManager;
            _mainWindow = mainWindow;
            _logger = logger;
            _patchingManager = patchingManager;
            _locker = locker;

            _modsFilter.Name = "Quest Mods";
            _modsFilter.Extensions.Add("qmod");
        }

        private static FileDialogFilter GetCosmeticFilter(FileCopyType copyType) 
        {
            return new FileDialogFilter
            {
                Name = copyType.NamePlural,
                Extensions = copyType.SupportedExtensions
            };
        }

        private void AddAllCosmeticFilters(OpenFileDialog dialog)
        {
            foreach(FileCopyType copyType in _otherFilesManager.CurrentDestinations)
            {
                dialog.Filters.Add(GetCosmeticFilter(copyType));
            }
        }

        /// <summary>
        /// Opens a browse dialog that has filters for all files supported by QuestPatcher.
        /// This includes qmod and all other file copies.
        /// </summary>
        /// <returns>A task that completes when the dialog has closed and the files have been imported</returns>
        public async Task ShowAllItemsBrowse()
        {
            OpenFileDialog dialog = ConstructDialog();

            // Add a filter for any file type that QuestPatcher supports
            // This includes qmod and all cosmetic/file copy types.
            FileDialogFilter allFiles = new()
            {
                Name = "All Allowed Files"
            };

            List<string> allExtensions = allFiles.Extensions;
            allExtensions.Add("qmod");
            foreach(FileCopyType copyType in _otherFilesManager.CurrentDestinations)
            {
                allExtensions.AddRange(copyType.SupportedExtensions);
            }

            dialog.Filters.Add(allFiles);
            dialog.Filters.Add(_modsFilter);
            AddAllCosmeticFilters(dialog);

            await ShowDialogAndHandleResult(dialog);
        }

        /// <summary>
        /// Opens a browse dialog for installing mods only.
        /// </summary>
        /// <returns>A task that completes when the dialog has closed and the files have been imported</returns>
        public async Task ShowModsBrowse()
        {
            OpenFileDialog dialog = ConstructDialog();
            dialog.Filters.Add(_modsFilter);
            await ShowDialogAndHandleResult(dialog);
        }

        /// <summary>
        /// Opens a browse dialog for installing this particular type of file copy/cosmetic.
        /// </summary>
        /// <param name="cosmeticType"></param>
        /// <returns>A task that completes when the dialog has closed and the files have been imported</returns>
        public async Task ShowFileCopyBrowse(FileCopyType cosmeticType)
        {
            OpenFileDialog dialog = ConstructDialog();
            dialog.Filters.Add(GetCosmeticFilter(cosmeticType));
            await ShowDialogAndHandleResult(dialog);
        }

        private static OpenFileDialog ConstructDialog()
        {
            return new OpenFileDialog()
            {
                AllowMultiple = true
            };
        }

        private async Task ShowDialogAndHandleResult(OpenFileDialog dialog)
        {
            string[] files = await dialog.ShowAsync(_mainWindow);
            if (files == null)
            {
                return;
            }

            await AttemptImportFiles(files);
        }

        /// <summary>
        /// Imports multiple files, and finds what type they are first.
        /// Will prompt the user with any errors while importing the files.
        /// If a list of files is already importing, these files will be added to the queue
        /// </summary>
        /// <param name="files">The paths of the files to import</param>
        public async Task AttemptImportFiles(ICollection<string> files)
        {
            bool queueExisted = _currentImportQueue != null;
            if(_currentImportQueue == null)
            {
                _currentImportQueue = new Queue<string>();
            }

            // Append all files to the new or existing queue
            _logger.Debug($"Enqueuing {files.Count} files");
            foreach (string file in files)
            {
                _currentImportQueue.Enqueue(file);
            }

            // If a queue already existed, that will be processed with our enqueued files, so we can stop here
            if (queueExisted)
            {
                _logger.Debug("Queue is already being processed");
                return;
            }

            // Otherwise, we process the current queue
            _logger.Debug("Processing queue . . .");

            // Do nothing if attempting to import files when operations are ongoing that are not file imports
            // TODO: Ideally this would wait until the lock is free and then continue
            if(!_locker.IsFree)
            {
                _logger.Error("Failed to process files: Operations are still ongoing");
                _currentImportQueue = null;
                return;
            }
            _locker.StartOperation();
            try
            {
                await ProcessImportQueue();
            }
            finally
            {
                _locker.FinishOperation();
                _currentImportQueue = null;
            }
        }

        /// <summary>
        /// Processes the current import queue until it reaches zero in size.
        /// Displays exceptions for any failed files
        /// </summary>
        private async Task ProcessImportQueue()
        {
            if (_currentImportQueue == null)
            {
                throw new InvalidOperationException("Cannot process import queue if there is no import queue assigned");
            }

            // Attempt to import each file, and catch the exceptions if any to display them below
            Dictionary<string, Exception> failedFiles = new();
            int totalProcessed = 0; // We cannot know how many files were enqueued in total, so we keep track of that here
            while(_currentImportQueue.TryDequeue(out string? path))
            {
                totalProcessed++;
                try
                {
                    _logger.Information($"Importing {path} . . .");
                    await ImportUnknownFile(path);
                }
                catch (Exception ex)
                {
                    failedFiles[path] = ex;
                }
            }
            _currentImportQueue = null; // New files added should go to a new queue

            _logger.Information($"{totalProcessed - failedFiles.Count}/{totalProcessed} files imported successfully");

            if (failedFiles.Count == 0) { return; }

            bool multiple = failedFiles.Count > 1;

            DialogBuilder builder = new()
            {
                Title = "Import Failed"
            };
            builder.HideCancelButton = true;

            if (multiple)
            {
                // Show the exceptions for multiple files in the logs to avoid a giagantic dialog
                builder.Text = "Multiple files failed to install. Check logs for details about each";
                foreach (KeyValuePair<string, Exception> pair in failedFiles)
                {
                    _logger.Error($"Failed to install {Path.GetFileName(pair.Key)}: {pair.Value.Message}");
                    _logger.Debug($"Full error: {pair.Value}");
                }
            }
            else
            {
                // Display single files with more detail for the user
                string filePath = failedFiles.Keys.First();
                Exception exception = failedFiles.Values.First();

                // Don't display the full stack trace for InstallationExceptions, since these are thrown by QP and are not bugs/issues
                if (exception is InstallationException)
                {
                    builder.Text = $"{Path.GetFileName(filePath)} failed to install: {exception.Message}";
                }
                else
                {
                    builder.Text = $"The file {Path.GetFileName(filePath)} failed to install";
                    builder.WithException(exception);
                }
                _logger.Error($"Failed to install {Path.GetFileName(filePath)}: {exception}");
            }

            await builder.OpenDialogue(_mainWindow);
        }

        /// <summary>
        /// Figures out what the given file is, and installs it accordingly.
        /// Throws an exception if the file cannot be installed by QuestPatcher.
        /// </summary>
        /// <param name="path">The path of file to import</param>
        private async Task ImportUnknownFile(string path)
        {
            string extension = Path.GetExtension(path).ToLower();

            // Attempt to install as a mod first
            if(extension == ".qmod")
            {
                await ImportMod(path);
                return;
            }

            // Attempt to copy the file to the quest as a map, hat or similar
            List<FileCopyType> copyTypes = _otherFilesManager.GetFileCopyTypes(extension);
            if(copyTypes.Count > 0)
            {
                FileCopyType copyType;
                if(copyTypes.Count > 1)
                {
                    // If there are multiple different file copy types for this file, prompt the user to decide what they want to import it as
                    FileCopyType? chosen = await OpenSelectCopyTypeDialog(copyTypes, path);
                    if(chosen == null)
                    {
                        _logger.Information($"Cancelling file {Path.GetFileName(path)}");
                        return;
                    }
                    else
                    {
                        copyType = chosen;
                    }
                }
                else
                {
                    // Otherwise, just use the only type available
                    copyType = copyTypes[0];
                }

                await copyType.PerformCopy(path);
                return;
            }

            throw new InstallationException($"Unrecognised file type {extension}");
        }

        /// <summary>
        /// Opens a dialog to allow the user to choose between multiple different file copy destinations to import a file as.
        /// </summary>
        /// <param name="copyTypes">The available file copy types for this file</param>
        /// <param name="path">The path of the file</param>
        /// <returns>The selected FileCopyType, or null if the user pressed cancel/closed the dialog</returns>
        private async Task<FileCopyType?> OpenSelectCopyTypeDialog(List<FileCopyType> copyTypes, string path)
        {
            FileCopyType? selectedType = null;

            DialogBuilder builder = new()
            {
                Title = "Multiple Import Options",
                Text = $"{Path.GetFileName(path)} can be imported as multiple types of file. Please select what you would like it to be installed as.",
                HideOkButton = true,
                HideCancelButton = true
            };

            List<ButtonInfo> dialogButtons = new();
            foreach(FileCopyType copyType in copyTypes)
            {
                dialogButtons.Add(new ButtonInfo
                {
                    ReturnValue = true,
                    CloseDialogue = true,
                    OnClick = () =>
                    {
                        selectedType = copyType;
                    },
                    Text = copyType.NameSingular
                });
            }
            builder.WithButtons(dialogButtons);

            await builder.OpenDialogue(_mainWindow);
            return selectedType;
        }

        /// <summary>
        /// Imports then installs a mod.
        /// Will prompt to ask the user if they want to install the mod in the case that it is outdated
        /// </summary>
        /// <param name="path">The path of the mod</param>
        private async Task ImportMod(string path)
        {
            // Import the mod file and copy it to the quest
            Mod mod = await _modManager.LoadMod(path);

            Debug.Assert(_patchingManager.InstalledApp != null);

            // Prompt the user for outdated mods instead of enabling them automatically
            if(mod.PackageVersion != _patchingManager.InstalledApp.Version)
            {
                DialogBuilder builder = new()
                {
                    Title = "Outdated Mod",
                    Text = $"The mod just installed is for version {mod.PackageVersion} of your app, however you have {_patchingManager.InstalledApp.Version}. Enabling the mod may crash the game, or not work."
                };
                builder.OkButton.Text = "Enable Now";
                builder.CancelButton.Text = "Cancel";

                if(!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            // Automatically install the mod once it has been imported
            // TODO: Is this desirable? Would it make sense to require it to be enabled manually
            await _modManager.InstallMod(mod);
        }
    }
}
