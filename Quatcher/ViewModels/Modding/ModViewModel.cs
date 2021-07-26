﻿using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Quatcher.Views;
using Quatcher.Models;
using Quatcher.Core.Modding;
using Quatcher.Core.Patching;
using System.IO;
using System.Diagnostics;

namespace Quatcher.ViewModels.Modding
{
    /// <summary>
    /// Wrapper around a mod used to display it within the UI and add some prompts.
    /// There might be a better way to do this, not completely sure.
    /// </summary>
    public class ModViewModel : ViewModelBase
    {
        public string Name => Inner.Name;
        public string Author => Inner.Porter == null ? $"(By {Inner.Author})" : $"(By {Inner.Author} - ported by {Inner.Porter})";

        public string Version => $"v{Inner.Version}";

        public string? Description => Inner.Description;
        public Bitmap? CoverImage { get; }

        public bool IsInstalled
        {
            get => _isToggling ? !Inner.IsInstalled : Inner.IsInstalled;
            set
            {
                if (value != Inner.IsInstalled)
                {
                    OnToggle(value);
                }
            }
        }

        public Mod Inner { get; }

        public OperationLocker Locker { get; }

        private readonly ModManager _modManager;
        private readonly PatchingManager _patchingManager;
        private readonly Window _mainWindow;

        private bool _isToggling; // Used to temporarily display the mod with the new toggle value until the toggle succeeds or fails

        public ModViewModel(Mod inner, ModManager modManager, PatchingManager patchingManager, Window mainWindow, OperationLocker locker)
        {
            Inner = inner;
            Locker = locker;
            _modManager = modManager;
            _patchingManager = patchingManager;
            _mainWindow = mainWindow;

            inner.PropertyChanged += (_, args) =>
            {
                if (!_isToggling)
                {
                    if (args.PropertyName == nameof(Inner.IsInstalled))
                    {
                        this.RaisePropertyChanged(nameof(IsInstalled));
                    }
                }
            };

            // Load the cover image, and just silently fail if loading it fails.
            // It shouldn't fail unless the mod has a corrupt cover image. No cover image or missing cover image is handled separately
            if (inner.CoverImage != null)
            {
                try
                {
                    using MemoryStream coverStream = new(inner.CoverImage);
                    CoverImage = new Bitmap(coverStream);
                }
                catch (Exception) { }
            }
        }

        private async void OnToggle(bool installed)
        {
            Locker.StartOperation();
            try
            {
                _isToggling = true;
                if (installed)
                {
                    await InstallSafely();
                }
                else
                {
                    await UninstallSafely();
                }
            }
            finally
            {
                Locker.FinishOperation();
                _isToggling = false;
                this.RaisePropertyChanged(nameof(IsInstalled));
            }
        }

        /// <summary>
        /// Installs the inner mod, and handles any errors.
        /// Also shows an outdated prompt for mods which aren't for the installed app version.
        /// </summary>
        private async Task InstallSafely()
        {
            Debug.Assert(_patchingManager.InstalledApp != null);
            // Check game version, and prompt if it is incorrect to avoid users installing mods that may crash their game
            if(Inner.PackageVersion != _patchingManager.InstalledApp.Version)
            {
                DialogBuilder builder = new()
                {
                    Title = "Outdated Mod",
                    Text = $"The mod you are trying to install is for game version {Inner.PackageVersion}, however you have {_patchingManager.InstalledApp.Version}. The mod may fail to load, it may crash the game, or it might even work just fine."
                };
                builder.OkButton.Text = "Continue Anyway";

                if(!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            try
            {
                await _modManager.InstallMod(Inner);
            }
            catch (Exception ex)
            {
                await ShowFailDialog("Failed to install mod", ex);
            }
        }

        /// <summary>
        /// Uninstalls the mod, and handles any errors with exception dialogs
        /// </summary>
        /// <returns></returns>
        private async Task<bool> UninstallSafely()
        {
            List<Mod> dependingOn = _modManager.FindModsDependingOn(Inner, true);
            // If the mod is depended on by other installed mods, we should ask the user before uninstalling it, since these mods will fail to load without it
            // This is a bit of a mess to make it work with both a both singular and plural number of mods
            if(dependingOn.Count > 0)
            {
                bool multiple = dependingOn.Count > 1;
                StringBuilder message = new(multiple ? "The mods " : "The mod ");
                for(int i = 0; i < dependingOn.Count; i++)
                {
                    if(i > 0)
                    {
                        if(i == dependingOn.Count - 1)
                        {
                            message.Append(" and ");
                        }
                        else
                        {
                            message.Append(", ");
                        }
                    }
                    message.Append(dependingOn[i].Name);
                }
                message.Append(multiple ? " depend" : " depends");
                message.Append(" on this mod. If the mod is uninstalled, ");
                message.Append(multiple ? "these mods" : "this mod");
                message.Append(" will most likely not work");

                DialogBuilder builder = new()
                {
                    Title = "Mod Depended On",
                    Text = message.ToString()
                };
                builder.OkButton.Text = "Continue Anyway";

                if(!await builder.OpenDialogue(_mainWindow))
                {
                    return false;
                }
            }

            try
            {
                await _modManager.UninstallMod(Inner);
                return true;
            }
            catch (Exception ex)
            {
                await ShowFailDialog("Failed to uninstall mod", ex);
                return false;
            }
        }

        public async void OnDelete()
        {
            Locker.StartOperation();
            try
            {
                // Always uninstall mods before deleting.
                // DeleteMod does this is the mod is installed, but we want to use our "safe" removal method to make sure that no mods depend on this one
                if (Inner.IsInstalled)
                {
                    if (!await UninstallSafely())
                    {
                        return;
                    }
                }

                await _modManager.DeleteMod(Inner);
            }
            catch (Exception ex)
            {
                await ShowFailDialog("Failed to delete mod", ex);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        /// <summary>
        /// Displays a dialog box with the specified exception and title.
        /// The text in the dialog will be the exception's message.
        /// The dialog will display the stack trace of the exception, unless it is an InstallationException
        /// </summary>
        /// <param name="title">Title of the dialog</param>
        /// <param name="ex">Exception to display</param>
        private async Task ShowFailDialog(string title, Exception ex)
        {
            DialogBuilder builder = new()
            {
                Title = title,
                Text = ex.Message,
                HideCancelButton = true
            };

            // InstallationExceptions are thrown by Quatcher itself to avoid certain conditions like installing on the wrong game
            // Displaying the stack traces for them isn't very helpful, since they aren't bugs/problems with QP
            if (ex is not InstallationException)
            {
                builder.WithException(ex);
            }
            await builder.OpenDialogue(_mainWindow);
        }
    }
}
