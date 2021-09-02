using System;
using Avalonia.Controls;
using Serilog.Core;
using Serilog.Events;

namespace QuestPatcher
{
    /// <summary>
    /// Handles logging to a TextBox
    /// </summary>
    public class TextBoxSink : ILogEventSink
    {
        private Action<string>? _addLine;
        
        public void Emit(LogEvent logEvent)
        {
            if (_addLine != null)
            {
                _addLine(logEvent.RenderMessage());
            }
        }

        public void Init(Action<string> addLine)
        {
            if (_addLine == null)
            {
                _addLine = addLine;
            }
            else
            {
                throw new InvalidOperationException(
                    "Attempted to initialise text box sink when it had already been initialised");
            }
        }
    }
}