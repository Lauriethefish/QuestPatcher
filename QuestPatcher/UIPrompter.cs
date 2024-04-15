using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Resources;
using QuestPatcher.Services;
using QuestPatcher.ViewModels;
using QuestPatcher.Views;

namespace QuestPatcher
{
    public class UIPrompter : IUserPrompter
    {
        private Window? _mainWindow;
        private Config? _config;
        private QuestPatcherUiService? _uiService;
        private SpecialFolders? _specialFolders;

        /// <summary>
        /// This exists instead of a constructor since the prompter must be immediately passed on QuestPatcherService's creation, so we initialise its members after the fact.
        /// Maybe there's a better workaround, but this works fine for now
        /// </summary>
        public void Init(Window mainWindow, Config config, QuestPatcherUiService uiService, SpecialFolders specialFolders)
        {
            _mainWindow = mainWindow;
            _config = config;
            _uiService = uiService;
            _specialFolders = specialFolders;
        }

        public Task<bool> PromptAppNotInstalled()
        {
            Debug.Assert(_config != null);

            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_AppNotInstalled_Title,
                Text = string.Format(Strings.Prompt_AppNotInstalled_Text, _config.AppId),
                HideOkButton = true
            };
            builder.CancelButton.Text = Strings.Generic_Close;
            builder.WithButtons(
                new ButtonInfo
                {
                    Text = Strings.Prompt_AppNotInstalled_ChangeApp,
                    CloseDialogue = true,
                    ReturnValue = true,
                    OnClick = async () =>
                    {
                        Debug.Assert(_uiService != null);
                        await _uiService.OpenChangeAppMenu(true);
                    }
                }
            );

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> PromptAdbDisconnect(DisconnectionType type)
        {
            var builder = new DialogBuilder();
            builder.OkButton.Text = Strings.Generic_Retry;

            switch (type)
            {
                case DisconnectionType.NoDevice:
                    builder.Title = Strings.Prompt_AdbDisconnect_NoDevice_Title;
                    builder.Text = Strings.Prompt_AdbDisconnect_NoDevice_Text;
                    builder.WithButtons(
                        new ButtonInfo
                        {
                            Text = Strings.Prompt_AdbDisconnect_NoDevice_SideQuest,
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
                    builder.Title = Strings.Prompt_AdbDisconnect_Offline_Title;
                    builder.Text = Strings.Prompt_AdbDisconnect_Offline_Text;
                    break;
                case DisconnectionType.Unauthorized:
                    builder.Title = Strings.Prompt_AdbDisconnect_Unauthorized_Title;
                    builder.Text = Strings.Prompt_AdbDisconnect_Unauthorized_Text;
                    break;
                default:
                    throw new NotImplementedException($"Variant {type} has no fallback/dialogue box");
            }

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> PromptUnstrippedUnityUnavailable()
        {
            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_NoUnstrippedUnity_Title,
                Text = Strings.Prompt_NoUnstrippedUnity_Text
            };
            builder.OkButton.Text = Strings.Generic_ContinueAnyway;

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> Prompt32Bit()
        {
            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_32Bit_Title,
                Text = Strings.Prompt_32Bit_Text
            };
            builder.OkButton.Text = Strings.Generic_ContinueAnyway;

            return builder.OpenDialogue(_mainWindow);
        }

        public Task<bool> PromptUnknownModLoader()
        {
            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_UnknownModLoader_Title,
                Text = Strings.Prompt_UnknownModLoader_Text
            };
            builder.OkButton.Text = Strings.Generic_ContinueAnyway;

            return builder.OpenDialogue(_mainWindow);
        }

        public Task PromptUpgradeFromOld()
        {
            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_UpgradeFromOld_Title,
                Text = Strings.Prompt_UpgradeFromOld_Text,
                HideCancelButton = true
            };

            return builder.OpenDialogue(_mainWindow);
        }

        public async Task<AdbDevice?> PromptSelectDevice(List<AdbDevice> devices)
        {
            var viewModel = new SelectDeviceWindowViewModel(devices);
            var window = new SelectDeviceWindow
            {
                DataContext = viewModel,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            viewModel.DeviceSelected += (_, device) => window.Close();
            await window.ShowDialog(_mainWindow!);

            return viewModel.SelectedDevice;
        }
    }
}
