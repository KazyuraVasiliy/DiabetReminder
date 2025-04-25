using Microsoft.Extensions.Logging;

namespace Tests
{
    public class Logger<T> : ILogger<T>
    {
        public LinkedList<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return new DummyDisposable();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.AddLast(formatter(state, exception));
        }
    }
}
