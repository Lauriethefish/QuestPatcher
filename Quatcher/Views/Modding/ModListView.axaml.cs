using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Quatcher.Views.Modding
{
    public class ModListView : UserControl
    {
        public ModListView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
