using System;
using System.Collections.Generic;
using QuestPatcher.Core;

namespace QuestPatcher.ViewModels
{
    public class SelectDeviceWindowViewModel : ViewModelBase
    {
        public List<AdbDevice> Devices { get; }

        public AdbDevice? SelectedDevice { get; set; } = null;

        public event EventHandler<AdbDevice>? DeviceSelected;

        public SelectDeviceWindowViewModel(List<AdbDevice> adbDevices)
        {
            Devices = adbDevices;
        }

        public void OnSelectDevice()
        {
            if (SelectedDevice != null)
            {
                DeviceSelected?.Invoke(null, SelectedDevice);
            }
        }
    }
}
