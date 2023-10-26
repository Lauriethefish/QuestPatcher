using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QuestPatcher.Views
{
    public class LoggingView : UserControl
    {
        private TextBox? _loggingBox;

        public LoggingView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            _loggingBox = this.FindControl<TextBox>("LoggingBox");
            // Scroll the logging box to the bottom whenever new text is added
            _loggingBox.PropertyChanged += (_, args) =>
            {
                if (args.Property.Name == nameof(_loggingBox.Text))
                {
                    _loggingBox.CaretIndex = int.MaxValue;
                }
            };
        }
    }
}
