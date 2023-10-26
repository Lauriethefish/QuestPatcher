using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuestPatcher.Models
{
    /// <summary>
    /// Handles locking sections of the UI while operations are being completed.
    /// NOT THREAD SAFE! Only used on the UI thread to avoid uninstalling the app while installing a mod, or patching while doing a quick fix.
    /// 
    /// This could be implemented as part of QuestPatcherService, however I'd prefer to keep it separate, since this class is more oriented around the current UI, and other implementators can just create their own.
    /// </summary>
    public class OperationLocker : INotifyPropertyChanged
    {
        public bool IsFree
        {
            get => _isFree;
            private set
            {
                if (_isFree != value)
                {
                    _isFree = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool _isFree = true;

        public bool IsAdbAvailable
        {
            get => _isAdbAvailable;
            private set
            {
                if (_isAdbAvailable != value)
                {
                    _isAdbAvailable = value;
                    NotifyPropertyChanged();
                }
            }
        }
        private bool _isAdbAvailable = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Attempts to get hold of the lock, and if failed throws an exception.
        /// The idea is that UI elements should not be clickable while operations are in progress.
        /// Calling this method should be immediately followed by try finally
        /// </summary>
        /// <param name="preventsAdb">Whether starting/stopping an ADB log will be disabled during this operation</param>
        /// <returns>Whether starting the operation was successful</returns>
        public void StartOperation(bool preventsAdb = false)
        {
            if (!IsFree)
            {
                throw new Exception("Attempted to start operation when one was in progress");
            }

            IsFree = false;
            IsAdbAvailable = !preventsAdb;
        }

        /// <summary>
        /// Finishes an operation, making the lock & ADB lock available.
        /// </summary>
        public void FinishOperation()
        {
            IsFree = true;
            IsAdbAvailable = true;
        }
    }
}
