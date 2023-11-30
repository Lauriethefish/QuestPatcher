using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace QuestPatcher
{
    /// <summary>
    /// Used to record Avalonia log information into a Serilog log file.
    /// </summary>
    internal class SerilogSink : ILogSink
    {
        /// <summary>
        /// The minimum level for logs to be copied to serilog.
        /// </summary>
        public Serilog.Events.LogEventLevel LogLevel { get; set; } = Serilog.Events.LogEventLevel.Warning;

        /// <summary>
        /// The logger to log to.
        /// </summary>
        public ILogger? Logger { get; set; }

        private readonly MessageTemplateParser _templateParser = new MessageTemplateParser();

        public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area)
        {
            return GetSerilogLevel(level) > LogLevel;
        }

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
        {
            Log(level, area, source, messageTemplate, null);
        }

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate, params object?[]? propertyValues)
        {
            if (!IsEnabled(level, area) || Logger == null)
            {
                return;
            }

            // Prefix avalonia related logs so that we know they're not from QP proper.
            var parsedTemplate = _templateParser.Parse("[Avalonia] " + messageTemplate);


            IEnumerable<LogEventProperty> properties;
            if (propertyValues != null)
            {
                // Convert the properties into the Serilog format.
                properties = parsedTemplate.Tokens
                    .Where(token => token is PropertyToken)
                    .Select(token => ((PropertyToken) token).PropertyName)
                    .Zip(propertyValues)
                    // Fallback message for missing properties.
                    .Select(pair => new LogEventProperty(pair.First, new ScalarValue(pair.Second ?? "<No value given>")));
            }
            else
            {
                properties = Enumerable.Empty<LogEventProperty>();
            }

            var logEvent = new LogEvent(DateTime.Now,
                GetSerilogLevel(level),
                null,
                parsedTemplate,
                properties
            );

            Logger.Write(logEvent);
        }

        /// <summary>
        /// Converts an Avalonia log level to the appropriate serilog level. 
        /// </summary>
        /// <param name="level">The Avalonia level to convert.</param>
        /// <returns>the Serilog log level.</returns>
        private Serilog.Events.LogEventLevel GetSerilogLevel(Avalonia.Logging.LogEventLevel level)
        {
            return level switch
            {
                Avalonia.Logging.LogEventLevel.Verbose => Serilog.Events.LogEventLevel.Verbose,
                Avalonia.Logging.LogEventLevel.Debug => Serilog.Events.LogEventLevel.Debug,
                Avalonia.Logging.LogEventLevel.Information => Serilog.Events.LogEventLevel.Information,
                Avalonia.Logging.LogEventLevel.Warning => Serilog.Events.LogEventLevel.Warning,
                Avalonia.Logging.LogEventLevel.Error => Serilog.Events.LogEventLevel.Error,
                Avalonia.Logging.LogEventLevel.Fatal => Serilog.Events.LogEventLevel.Fatal,
                _ => Serilog.Events.LogEventLevel.Information // Fallback to Information
            };
        }
    }
}
