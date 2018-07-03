﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Foundatio.Logging.Xunit;
using Foundatio.Utility;
using Microsoft.Extensions.Logging;

namespace SoftwarePioniere.EventStore.Tests
{
    internal class TestLogger2 : ILogger
    {
        private readonly TestLoggerSerilogFactory _loggerFactory;
        private readonly string _categoryName;

        public TestLogger2(string categoryName, TestLoggerSerilogFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _categoryName = categoryName;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!_loggerFactory.IsEnabled(_categoryName, logLevel))
                return;

            var scopes = CurrentScopeStack.Reverse().ToArray();
            var logEntry = new LogEntry
            {
                Date = SystemClock.UtcNow,
                LogLevel = logLevel,
                EventId = eventId,
                State = state,
                Exception = exception,
                Message = formatter(state, exception),
                CategoryName = _categoryName,
                Scopes = scopes
            };

            switch (state)
            {
                //case LogData logData:
                //    logEntry.Properties["CallerMemberName"] = logData.MemberName;
                //    logEntry.Properties["CallerFilePath"] = logData.FilePath;
                //    logEntry.Properties["CallerLineNumber"] = logData.LineNumber;

                //    foreach (var property in logData.Properties)
                //        logEntry.Properties[property.Key] = property.Value;
                //    break;
                case IDictionary<string, object> logDictionary:
                    foreach (var property in logDictionary)
                        logEntry.Properties[property.Key] = property.Value;
                    break;
            }

            foreach (object scope in scopes)
            {
                var scopeData = scope as IDictionary<string, object>;
                if (scopeData == null)
                    continue;

                foreach (var property in scopeData)
                    logEntry.Properties[property.Key] = property.Value;
            }

            _loggerFactory.AddLogEntry(logEntry);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _loggerFactory.MinimumLevel;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return Push(state);
        }

        public IDisposable BeginScope<TState, TScope>(Func<TState, TScope> scopeFactory, TState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return Push(scopeFactory(state));
        }

        // ReSharper disable once InconsistentNaming
        private static readonly AsyncLocal<Wrapper> _currentScopeStack = new AsyncLocal<Wrapper>();

        private sealed class Wrapper
        {
            public ImmutableStack<object> Value { get; set; }
        }

        private static ImmutableStack<object> CurrentScopeStack
        {
            get => _currentScopeStack.Value?.Value ?? ImmutableStack.Create<object>();
            set => _currentScopeStack.Value = new Wrapper { Value = value };
        }

        private static IDisposable Push(object state)
        {
            CurrentScopeStack = CurrentScopeStack.Push(state);
            return new DisposableAction(Pop);
        }

        private static void Pop()
        {
            CurrentScopeStack = CurrentScopeStack.Pop();
        }
    }
}
