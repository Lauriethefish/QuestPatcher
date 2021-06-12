using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QuestPatcher.Views
{
    public class SelectAppWindow : Window
    {
        public SelectAppWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
