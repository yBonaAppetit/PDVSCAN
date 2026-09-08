using System.Media;
using System.Threading.Channels;

namespace PdvBarcodeFilter;

public sealed class ScanCoordinator : IDisposable
{
    private readonly FilterSettings _settings;
    private readonly ScanProcessor _processor;
    private readonly IKeyboardSender _sender;
    private readonly Action<FilterNotification> _notify;
    private readonly ScannerFloodGuard _floodGuard;
    private readonly RealtimeDiagnosticHub _diagnostics;
    private readonly Channel<CapturedScan> _channel;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private bool _disposed;
    private long _queuedScans;
    private long _validScans;
    private long _invalidScans;
    private long _sentScans;
    private long _sendFailures;

    public ScanCoordinator(
        FilterSettings settings,
        IKeyboardSender sender,
        Action<FilterNotification> notify,
        RealtimeDiagnosticHub diagnostics)
    {
        _settings = settings;
        _processor = new ScanProcessor(settings);
        _sender = sender;
        _notify = notify;
        _diagnostics = diagnostics;
        _floodGuard = new ScannerFloodGuard(settings);
        _channel = Channel.CreateUnbounded<CapturedScan>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ProcessLoopAsync);
    }

    public bool TryPost(CapturedScan scan)
    {
        if (_settings.KeyboardPassthrough &&
            (scan.Overflowed || scan.TimedOut || !_processor.HasValidStructure(scan.RawCode)))
        {
            if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write(
                    "KEYBOARD_PASSTHROUGH",
                    $"Entrada comum não enfileirada | length={scan.RawCode.Length} overflow={scan.Overflowed} timeout={scan.TimedOut}");
            }
            return false;
        }

        Post(scan);
        return true;
    }

    public void Post(CapturedScan scan)
    {
        Interlocked.Increment(ref _queuedScans);
        if (_diagnostics.DetailedInputLogging)
        {
            _diagnostics.Write(
                "QUEUE_POST",
                $"length={scan.RawCode.Length} overflow={scan.Overflowed} timeout={scan.TimedOut} raw={_diagnostics.DisplayValue(scan.RawCode)}");
        }
        if (!_channel.Writer.TryWrite(scan))
        {
            SignalError();
            _diagnostics.Write("QUEUE_ERROR", "Writer recusou a leitura porque a fila está encerrada.");
        }
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            await foreach (var scan in _channel.Reader.ReadAllAsync(_cancellation.Token))
            {
                if (_diagnostics.DetailedInputLogging)
                {
                    _diagnostics.Write("QUEUE_READ", $"Processando leitura | length={scan.RawCode.Length}");
                }
                var floodDecision = _floodGuard.Evaluate(Environment.TickCount64);
                if (floodDecision == FloodDecision.Blocked)
                {
                    Interlocked.Increment(ref _invalidScans);
                    _diagnostics.Write("SCAN_REJECT", "Ignorada durante cooldown de flood.");
                    continue;
                }

                if (floodDecision == FloodDecision.JustBlocked)
                {
                    const string message = "Scanner bloqueado temporariamente por excesso de leituras.";
                    _notify(new FilterNotification(FilterNotificationKind.ScannerBlocked, message));
                    SignalError();
                    Interlocked.Increment(ref _invalidScans);
                    _diagnostics.Write("SCAN_REJECT", "Proteção contra flood acionada.");
                    continue;
                }

                if (scan.Overflowed)
                {
                    const string message = "Leitura excedeu o tamanho máximo configurado.";
                    _notify(new FilterNotification(FilterNotificationKind.InvalidScan, message));
                    SignalError();
                    Interlocked.Increment(ref _invalidScans);
                    _diagnostics.Write("SCAN_REJECT", "Leitura excedeu MaxScanLength.");
                    continue;
                }

                if (scan.TimedOut)
                {
                    const string message = "Leitura incompleta descartada por tempo limite.";
                    _notify(new FilterNotification(FilterNotificationKind.InvalidScan, message));
                    SignalError();
                    Interlocked.Increment(ref _invalidScans);
                    _diagnostics.Write("SCAN_REJECT", "Leitura expirou antes do ENTER.");
                    continue;
                }

                var result = _processor.Process(scan.RawCode);
                if (!result.IsValid || result.Output is null)
                {
                    Interlocked.Increment(ref _invalidScans);
                    _notify(new FilterNotification(
                        FilterNotificationKind.InvalidScan,
                        "Leitura inválida descartada."));
                    SignalError();
                    var pipeCount = scan.RawCode.Count(character => character == '|');
                    var braceCount = scan.RawCode.Count(character => character == '}');
                    _diagnostics.Write(
                        "PARSE_REJECT",
                        $"motivo={result.Error} pipe_count={pipeCount} brace_count={braceCount} raw={_diagnostics.DisplayValue(scan.RawCode)}");
                    continue;
                }

                Interlocked.Increment(ref _validScans);
                try
                {
                    var targetProcess = ForegroundTarget.GetProcessName();
                    var selection = result.UsedAuxiliary ? "AUXILIAR" : "PADRAO";
                    _diagnostics.Write(
                        "PARSE_OK",
                        $"selection={selection} tentativa={result.ConsecutiveCount} repeated={result.IsRepeated} output={_diagnostics.DisplayValue(result.Output)}");
                    _diagnostics.Write(
                        "SEND_BEGIN",
                        $"target={targetProcess ?? "<desconhecido>"} replace_field={scan.ReplaceCurrentField} " +
                        $"append_enter={_settings.SendEnter} output={_diagnostics.DisplayValue(result.Output)}");
                    _sender.Send(result.Output, _settings.SendEnter, scan.ReplaceCurrentField);
                    Interlocked.Increment(ref _sentScans);
                    _diagnostics.Write("SEND_OK", $"target={targetProcess ?? "<desconhecido>"}");
                    _notify(new FilterNotification(
                        result.UsedAuxiliary
                            ? FilterNotificationKind.AuxiliarySent
                            : FilterNotificationKind.StandardSent,
                        result.UsedAuxiliary
                            ? "Código auxiliar enviado."
                            : "Código padrão enviado."));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _sendFailures);
                    _notify(new FilterNotification(
                        FilterNotificationKind.Error,
                        "Falha ao enviar o código tratado."));
                    SignalError();
                    _diagnostics.Write(
                        "SEND_ERROR",
                        $"{ex.GetType().Name}: {ex.Message} | win32={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SignalError();
            _diagnostics.Write("WORKER_ERROR", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public ScanCoordinatorStatistics GetStatistics() => new(
        Interlocked.Read(ref _queuedScans),
        Interlocked.Read(ref _validScans),
        Interlocked.Read(ref _invalidScans),
        Interlocked.Read(ref _sentScans),
        Interlocked.Read(ref _sendFailures));

    private void SignalError()
    {
        if (_settings.BeepOnError)
        {
            try
            {
                SystemSounds.Beep.Play();
            }
            catch
            {
                // O beep é apenas informativo e não pode interromper o filtro.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        if (!_worker.Wait(TimeSpan.FromMilliseconds(250)))
        {
            _cancellation.Cancel();
            try
            {
                _worker.Wait(TimeSpan.FromMilliseconds(250));
            }
            catch (AggregateException aggregate) when (
                aggregate.InnerExceptions.All(exception => exception is OperationCanceledException))
            {
            }
        }

        _cancellation.Dispose();
    }
}

public sealed record ScanCoordinatorStatistics(
    long QueuedScans,
    long ValidScans,
    long InvalidScans,
    long SentScans,
    long SendFailures);
