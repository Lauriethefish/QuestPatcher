using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace QuestPatcher
{
    public class FluentWindow : Window
    {
        public FluentWindow()
        {
            TransparencyLevelHint = WindowTransparencyLevel.AcrylicBlur;
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            ExtendClientAreaChromeHints =
                Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome |
                Avalonia.Platform.ExtendClientAreaChromeHints.OSXThickTitleBar;
        }
    }
}
