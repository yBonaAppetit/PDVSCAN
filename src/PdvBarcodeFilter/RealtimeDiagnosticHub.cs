using System.Text;
using System.Threading.Channels;

namespace PdvBarcodeFilter;

public sealed class RealtimeDiagnosticHub : IDisposable
{
    private readonly bool _enabled;
    private readonly bool _logRawData;
    private readonly bool _detailedInputLogging;
    private readonly long _maxBytes;
    private readonly object _sync = new();
    private readonly Queue<string> _recentLines = new();
    private readonly Channel<string>? _pendingLines;
    private readonly StreamWriter? _writer;
    private readonly Task? _writerTask;
    private bool _sizeLimitReported;
    private bool _disposed;
    private long _sequence;
    private long _estimatedBytes;

    public RealtimeDiagnosticHub(FilterSettings settings)
    {
        _enabled = settings.RealtimeDiagnosticsEnabled;
        _logRawData = settings.DiagnosticLogRawData;
        _detailedInputLogging = settings.DetailedInputLogging;
        _maxBytes = settings.DiagnosticMaxFileMb * 1024L * 1024L;
        DiagnosticDirectory = ResolveDirectory();
        DiagnosticFilePath = Path.Combine(
            DiagnosticDirectory,
            $"realtime-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

        if (_enabled)
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            _writer = new StreamWriter(
                new FileStream(DiagnosticFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            _pendingLines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _writerTask = Task.Run(WritePendingLinesAsync);
        }
    }

    public event Action<string>? LineAdded;

    public string DiagnosticDirectory { get; }

    public string DiagnosticFilePath { get; }

    public bool IsEnabled => _enabled;

    public bool LogRawData => _logRawData;

    public bool DetailedInputLogging => _detailedInputLogging;

    public void Write(string category, string message)
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _sequence);
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | #{sequence:D8} | T{Environment.CurrentManagedThreadId:D2} | {category} | {Sanitize(message)}";
        _pendingLines?.Writer.TryWrite(line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_sync)
        {
            return _recentLines.ToArray();
        }
    }

    public string DisplayValue(string value) =>
        _logRawData ? $"\"{Sanitize(value)}\"" : $"<oculto; comprimento={value.Length}>";

    public static string Sanitize(string value) =>
        value.Replace("\r", "\\r", StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal)
             .Replace("\t", "\\t", StringComparison.Ordinal);

    private async Task WritePendingLinesAsync()
    {
        if (_pendingLines is null || _writer is null)
        {
            return;
        }

        await foreach (var line in _pendingLines.Reader.ReadAllAsync())
        {
            var publishedLine = line;
            var lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (_estimatedBytes + lineBytes <= _maxBytes)
            {
                await _writer.WriteLineAsync(line).ConfigureAwait(false);
                _estimatedBytes += lineBytes;
            }
            else if (!_sizeLimitReported)
            {
                _sizeLimitReported = true;
                publishedLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | DIAGNOSTIC | Limite de arquivo atingido; gravação suspensa.";
                await _writer.WriteLineAsync(publishedLine).ConfigureAwait(false);
            }

            lock (_sync)
            {
                _recentLines.Enqueue(publishedLine);
                while (_recentLines.Count > 2500)
                {
                    _recentLines.Dequeue();
                }
            }

            try
            {
                LineAdded?.Invoke(publishedLine);
            }
            catch
            {
                // Uma janela de diagnóstico não pode afetar a captura.
            }
        }

        await _writer.FlushAsync().ConfigureAwait(false);
    }

    private static string ResolveDirectory()
    {
        var shared = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PdvBarcodeFilter",
            "diagnostics");
        try
        {
            Directory.CreateDirectory(shared);
            return shared;
        }
        catch
        {
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PdvBarcodeFilter",
                "diagnostics");
            Directory.CreateDirectory(local);
            return local;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pendingLines?.Writer.TryComplete();
        try
        {
            _writerTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // O encerramento do diagnóstico não pode manter o aplicativo aberto.
        }

        _writer?.Dispose();
    }
}
