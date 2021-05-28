using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using QuestPatcher.ViewModels;

namespace QuestPatcher.Views
{
    public class LoadedView : UserControl
    {
        public LoadedView()
        {
            InitializeComponent();

            PropertyChanged += (sender, args) =>
            {
                // Subscribe the drag and drop event in the ViewModel if/when it changes or is first assigned.
                // Unfortunately this event is unusual and it's not possible to subscribe to it using a binding
                if (args.Property.Name == nameof(DataContext))
                {
                    // We use as and null checks to not force LoadedViewModel to be the view model in the future
                    LoadedViewModel? oldViewModel = args.OldValue as LoadedViewModel;
                    if (oldViewModel != null)
                    {
                        RemoveHandler(DragDrop.DropEvent, oldViewModel.OnDragAndDrop);
                    }

                    LoadedViewModel? newViewModel = args.NewValue as LoadedViewModel;
                    if(newViewModel != null)
                    {
                        AddHandler(DragDrop.DropEvent, newViewModel.OnDragAndDrop);
                    }
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            
        }
    }
}
