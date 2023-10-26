using System;
using Serilog.Core;
using Serilog.Events;

namespace QuestPatcher
{
    class StringDelegateSink : ILogEventSink
    {
        private Action<string> _action;

        public StringDelegateSink(Action<string> action)
        {
            _action = action;
        }

        public void Emit(LogEvent logEvent)
        {
            _action(logEvent.RenderMessage());
        }
    }
}
