using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using QuestPatcher.Core;
using QuestPatcher.Core.Modding;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using ReactiveUI;

namespace QuestPatcher.ViewModels.Modding
{
    /// <summary>
    /// Wrapper around a mod used to display it within the UI and add some prompts.
    /// There might be a better way to do this, not completely sure.
    /// </summary>
    public class ModViewModel : ViewModelBase
    {
        public string Name => Mod.Name;
        public string Author => Mod.Porter == null ? string.Format(Strings.Mod_Author, Mod.Author) : string.Format(Strings.Mod_AuthorWithPorter, Mod.Author, Mod.Porter);

        public string Version => $"v{Mod.Version}";

        public string? Description => Mod.Description;
        public Bitmap? CoverImage { get; set; }

        public bool IsInstalled
        {
            get => _isToggling ? !Mod.IsInstalled : Mod.IsInstalled;
            set
            {
                if (value != Mod.IsInstalled)
                {
                    OnToggle(value);
                }
            }
        }

        public IMod Mod { get; }

        public OperationLocker Locker { get; }

        private readonly ModManager _modManager;
        private readonly InstallManager _installManager;
        private readonly Window _mainWindow;

        private bool _isToggling; // Used to temporarily display the mod with the new toggle value until the toggle succeeds or fails

        public ModViewModel(IMod mod, ModManager modManager, InstallManager installManager, Window mainWindow, OperationLocker locker)
        {
            Mod = mod;
            Locker = locker;
            _modManager = modManager;
            _installManager = installManager;
            _mainWindow = mainWindow;

            mod.PropertyChanged += (_, args) =>
            {
                if (!_isToggling)
                {
                    if (args.PropertyName == nameof(Mod.IsInstalled))
                    {
                        this.RaisePropertyChanged(nameof(IsInstalled));
                    }
                }
            };

            LoadCoverImage();
        }

        private async void LoadCoverImage()
        {
            try
            {
                using var coverStream = await Mod.OpenCover();
                if (coverStream != null)
                {
                    CoverImage = new Bitmap(coverStream);
                    this.RaisePropertyChanged(nameof(CoverImage));
                }
            }
            catch (Exception)
            {
                // ignored
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
                await _modManager.SaveMods();
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
            Debug.Assert(_installManager.InstalledApp != null);

            // Check that the modloader matches what we have installed
            if (Mod.ModLoader != _installManager.InstalledApp.ModLoader)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Mod_WrongModLoader_Title,
                    Text = String.Format(Strings.Mod_WrongModLoader_Text, Mod.ModLoader, _installManager.InstalledApp.ModLoader),
                    HideCancelButton = true
                };

                await builder.OpenDialogue(_mainWindow);
                return;
            }

            // Check game version, and prompt if it is incorrect to avoid users installing mods that may crash their game
            if (Mod.PackageVersion != null && Mod.PackageVersion != _installManager.InstalledApp.Version)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Mod_OutdatedMod_Title,
                    Text = string.Format(Strings.Mod_OutdatedMod_Text, Mod.PackageVersion, _installManager.InstalledApp.Version)
                };
                builder.OkButton.Text = Strings.Generic_ContinueAnyway;

                if (!await builder.OpenDialogue(_mainWindow))
                {
                    return;
                }
            }

            try
            {
                await Mod.Install();
            }
            catch (Exception ex)
            {
                await ShowFailDialog(Strings.Mod_InstallFailed, ex);
            }
        }

        /// <summary>
        /// Uninstalls the mod, and handles any errors with exception dialogs
        /// </summary>
        /// <returns></returns>
        private async Task<bool> UninstallSafely()
        {
            /*
            List<Mod> dependingOn = _modManager.FindModsDependingOn(Mod, true);
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
            }*/ // TODO: Reimplement ^^

            try
            {
                await Mod.Uninstall();
                return true;
            }
            catch (Exception ex)
            {
                await ShowFailDialog(Strings.Mod_UninstallFailed, ex);
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
                if (Mod.IsInstalled)
                {
                    if (!await UninstallSafely())
                    {
                        return;
                    }
                }

                await _modManager.DeleteMod(Mod);
                await _modManager.SaveMods();
            }
            catch (Exception ex)
            {
                await ShowFailDialog(Strings.Mod_DeleteFailed, ex);
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
            var builder = new DialogBuilder
            {
                Title = title,
                Text = ex.Message,
                HideCancelButton = true
            };

            // InstallationExceptions are thrown by QuestPatcher itself to avoid certain conditions like installing on the wrong game
            // Displaying the stack traces for them isn't very helpful, since they aren't bugs/problems with QP
            if (ex is not InstallationException)
            {
                builder.WithException(ex);
            }
            await builder.OpenDialogue(_mainWindow);
        }
    }
}
