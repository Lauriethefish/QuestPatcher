using Avalonia;
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

            _loggingBox = this.FindControl<TextBox>("loggingBox");
            // Scroll the logging box to the bottom whenever new text is added
            _loggingBox.PropertyChanged += (sender, args) =>
            {
                _loggingBox.CaretIndex = int.MaxValue;
            };
        }
    }
}
