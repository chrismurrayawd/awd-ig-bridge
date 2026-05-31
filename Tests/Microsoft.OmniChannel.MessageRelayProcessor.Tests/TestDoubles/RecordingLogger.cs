// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles
{
    /// <summary>
    /// An <see cref="ILogger{T}"/> that records every entry so tests can assert on log level / message / exception.
    /// Mirrors the private helper in the Instagram token tests (which is sealed/nested and cannot be shared across
    /// test projects), kept here so the relay tests can prove the loud-not-silent logging contract.
    /// </summary>
    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new List<LogEntry>();

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public sealed class LogEntry
        {
            public LogEntry(LogLevel level, string message, Exception exception)
            {
                Level = level;
                Message = message;
                Exception = exception;
            }

            public LogLevel Level { get; }

            public string Message { get; }

            public Exception Exception { get; }
        }
    }
}
