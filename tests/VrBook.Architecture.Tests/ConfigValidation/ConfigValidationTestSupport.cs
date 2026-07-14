using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VrBook.Architecture.Tests.ConfigValidation;

/// <summary>Minimal <see cref="IHostEnvironment"/> double so the VRB-200
/// validation carve-out (<c>IsDevelopment()</c>) can be exercised per env
/// without booting a host.</summary>
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "VrBook.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
}

/// <summary>Captures log entries so tests can assert on the VRB-200
/// <c>ConfigValidationPassed</c> info line and the Development dev-loopback
/// warning without a real sink.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public readonly record struct Entry(LogLevel Level, string Category, string Message);

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries
    {
        get { lock (_entries) { return _entries.ToList(); } }
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this);

    public void Dispose() { }

    private void Add(Entry entry)
    {
        lock (_entries) { _entries.Add(entry); }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly string _category;
        private readonly RecordingLoggerProvider _owner;

        public RecordingLogger(string category, RecordingLoggerProvider owner)
        {
            _category = category;
            _owner = owner;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _owner.Add(new Entry(logLevel, _category, formatter(state, exception)));
    }
}
