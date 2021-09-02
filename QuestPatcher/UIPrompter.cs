using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Views;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace QuestPatcher
{
    internal delegate void QuitDelegate();

    internal delegate Task OpenChangeAppMenuDelegate(bool quitIfNotSelected);

    public class UIPrompter : ICallbacks
    {
        private readonly Window _mainWindow;
        private readonly Config _config;
        private readonly SpecialFolders _specialFolders;

        internal QuitDelegate? OnQuit { get; set; }
        
        internal OpenChangeAppMenuDelegate? ChangeApp { get; set; }

        public UIPrompter(MainWindow mainWindow, Config config, SpecialFolders specialFolders)
        {
            _mainWindow = mainWindow;
            _config = config;
            _specialFolders = specialFolders;
        }

        public Task<bool> PromptAppNotInstalled()
        {
            DialogBuilder builder = new()
            {
                Title = "App Not Installed",
                Text = $"The selected app - {_config.AppId} - is not installed",
                HideOkButton = true,
                CancelButton = { Text = "Close" }
            };
            builder.WithButtons(
                new ButtonInfo
                {
                    Text = "Change App",
                    CloseDialogue = true,
                    ReturnValue = true,
                    OnClick = async () =>
                    {
                        if (ChangeApp is not null)
                        {
                            await ChangeApp(true);
                        }
                    }
                }
            );

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> PromptAdbDisconnect(DisconnectionType type)
        {
            DialogBuilder builder = new();
            builder.OkButton.Text = "Try Again";

            switch (type)
            {
                case DisconnectionType.NoDevice:
                    builder.Title = "Quest Not Connected";
                    builder.Text = "QuestPatcher could not detect your Quest.\nMake sure that your Quest is plugged in, and that you have setup developer mode as per the SideQuest installation instructions.";
                    builder.WithButtons(
                        new ButtonInfo
                        {
                            Text = "SideQuest Instructions",
                            OnClick = () =>
                            {
                                ProcessStartInfo psi = new()
                                {
                                    FileName = "https://sidequestvr.com/setup-howto",
                                    UseShellExecute = true
                                };
                                Process.Start(psi);
                            }
                        }
                    );
                    break;
                case DisconnectionType.DeviceOffline:
                    builder.Title = "Device Offline";
                    builder.Text = "Your Quest has been detected as offline.\nTry restarting your Quest and your PC";
                    break;
                case DisconnectionType.MultipleDevices:
                    builder.Title = "Multiple Devices Plugged In";
                    builder.Text = "Multiple Android devices are connected to your PC.\nPlease unplug all devices other than your Quest. (and turn off emulators such as BlueStacks)";
                    break;
                case DisconnectionType.Unauthorized:
                    builder.Title = "Device Unauthorized";
                    builder.Text = "Please press allow from this PC within the headset, even if you have done it before for SideQuest.";
                    break;
                default:
                    throw new NotImplementedException($"Variant {type} has no fallback/dialogue box");
            }

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> PromptUnstrippedUnityUnavailable()
        {
            DialogBuilder builder = new()
            {
                Title = "Missing libunity.so",
                Text = "No unstripped libunity.so is available for the app you have selected. " +
                       "This may mean that certain mods will not work correctly until one is added to the index. " +
                       "Proceed with caution - if you're updating from an older version, it is wise to wait for the latest version of your app to be added.",
                OkButton = { Text = "Continue Anyway" }
            };

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> Prompt32Bit()
        {
            DialogBuilder builder = new()
            {
                Title = "32 bit APK",
                Text =
                    "The app you are attempting to patch is 32 bit (armeabi-v7a). QuestPatcher supports a 32 version of QuestLoader, however most libraries like beatsaber-hook don't, unlesss you use a very old version. " +
                    "This will make modding much more difficult.",
                OkButton = { Text = "Continue Anyway" }
            };

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> PromptPauseBeforeCompile()
        {
            DialogBuilder builder = new()
            {
                Title = "Patching Paused",
                Text =
                    "The APK has been patched and will recompile/reinstall when you continue. Pressing cancel will immediately stop patching.",
                OkButton = { Text = "Continue" }
            };
            builder.WithButtons(new ButtonInfo
            {
                Text = "Show patched APK",
                OnClick = () =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _specialFolders.PatchingFolder,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            });

            return builder.OpenDialogue(_mainWindow);
        }

        public Task PromptUpgradeFromOld()
        {
            DialogBuilder builder = new()
            {
                Title = "Upgrading from QuestPatcher 1",
                Text = "It looks as though you've previously used QuestPatcher 1.\n\n" +
                    "Note that your mods from QuestPatcher 1 will be removed - this is deliberate as QuestPatcher 2 reworks mod installing to allow toggling of mods! " +
                    "To get your mods back, just reinstall them.\n\n" + 
                    "NOTE: All save data, custom maps and cosmetics will remain safe!",
                HideCancelButton = true
            };

            return builder.OpenDialogue(_mainWindow);
        }

        public void Quit()
        {
            OnQuit?.Invoke();
        }
    }
}
