using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QuestPatcher.Views.Modding
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
