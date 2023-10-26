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
                    if (args.OldValue is LoadedViewModel oldViewModel)
                    {
                        RemoveHandler(DragDrop.DropEvent, oldViewModel.OnDragAndDrop);
                    }

                    if (args.NewValue is LoadedViewModel newViewModel)
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
