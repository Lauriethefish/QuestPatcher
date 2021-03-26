using System.IO;
using System.Text;

namespace QuestPatcher
{
    // Used to allow us to log messages into the QuestPatcher window so that the user can see what it is doing.
    class WindowLogger : TextWriter
    {
        private MainWindow window;

        public WindowLogger(MainWindow window)
        {
            this.window = window;
        }

        public override Encoding Encoding => Encoding.UTF8;

        private void addText(string text)
        {
            window.LoggingBox.Text += text;
            window.LoggingBox.CaretIndex = int.MaxValue;
        }

        public override void Write(char value)
        {
            addText("" + value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            string str = new string(buffer, index, count);
            addText(str);
        }
    }
}
