using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace QuestPatcher
{
    class WindowLogger : TextWriter
    {
        private MainWindow window;

        public WindowLogger(MainWindow window)
        {
            this.window = window;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            window.LoggingBox.Text += value;
        }

        public override void Write(char[] buffer, int index, int count)
        {
            string str = new string(buffer, index, count);
            window.LoggingBox.Text += str;
        }
    }
}
