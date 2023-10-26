using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using QuestPatcher.ViewModels;

namespace QuestPatcher.Views
{
    public partial class OtherItemsView : UserControl
    {
        public OtherItemsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public async void FileDeleteBtn_Click(object? sender, RoutedEventArgs args)
        {
            Debug.Assert(sender != null); // Button press, so the sender is always a button
            string? fileName = ((Button) sender).DataContext as string;
            Debug.Assert(fileName != null); // The DataContext should always be set as a string
            if (DataContext is OtherItemsViewModel viewModel)
            {
                await viewModel.DeleteFiles(fileName);
            }
        }
    }
}
