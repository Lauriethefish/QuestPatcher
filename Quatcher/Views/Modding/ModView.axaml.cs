using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Quatcher.Views.Modding
{
    public partial class ModView : UserControl
    {
        public ModView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
