using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QuestPatcher.Views.Modding
{
    public partial class ManageModsView : UserControl
    {
        public ManageModsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
