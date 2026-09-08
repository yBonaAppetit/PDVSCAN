using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PdvBarcodeFilter;

public enum SimulatorSubmissionStatus
{
    Accepted,
    Disabled,
    Paused,
    Invalid,
    Unavailable,
    Error
}

public sealed record SimulatorSubmissionResult(
    SimulatorSubmissionStatus Status,
    string Message)
{
    public bool IsAccepted => Status == SimulatorSubmissionStatus.Accepted;

    public static SimulatorSubmissionResult Accepted(string message = "Leitura aceita pelo filtro.") =>
        new(SimulatorSubmissionStatus.Accepted, message);
}

internal sealed record SimulatorScanRequest(string RawCode);

public static class ScannerSimulatorProtocol
{
    public const string DefaultPipeName = "PdvBarcodeFilter.ScannerSimulator.v1";
    public const int MaximumRequestCharacters = 65_536;
}

public sealed class ScannerSimulatorServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<string, SimulatorSubmissionResult> _submit;
    private readonly Action<Exception>? _reportError;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _listenerTask;
    private bool _disposed;

    public ScannerSimulatorServer(
        Func<string, SimulatorSubmissionResult> submit,
        string? pipeName = null,
        Action<Exception>? reportError = null)
    {
        _submit = submit ?? throw new ArgumentNullException(nameof(submit));
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? ScannerSimulatorProtocol.DefaultPipeName
            : pipeName;
        _reportError = reportError;
        _listenerTask = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await ListenForOneClientAsync(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _reportError?.Invoke(ex);
                try
                {
                    await Task.Delay(250, _cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task ListenForOneClientAsync(CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        SimulatorSubmissionResult response;
        try
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) ||
                line.Length > ScannerSimulatorProtocol.MaximumRequestCharacters)
            {
                response = new SimulatorSubmissionResult(
                    SimulatorSubmissionStatus.Invalid,
                    "A leitura está vazia ou excede o limite permitido.");
            }
            else
            {
                var request = JsonSerializer.Deserialize<SimulatorScanRequest>(line);
                response = request is null || string.IsNullOrWhiteSpace(request.RawCode)
                    ? new SimulatorSubmissionResult(
                        SimulatorSubmissionStatus.Invalid,
                        "A leitura enviada é inválida.")
                    : _submit(request.RawCode);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            response = new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Invalid,
                $"Solicitação inválida: {ex.Message}");
        }

        await writer.WriteLineAsync(
            JsonSerializer.Serialize(response).AsMemory(),
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        try
        {
            _listenerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException aggregate) when (
            aggregate.InnerExceptions.All(exception => exception is OperationCanceledException))
        {
        }

        _cancellation.Dispose();
    }
}

public static class ScannerSimulatorClient
{
    public static async Task<SimulatorSubmissionResult> SendAsync(
        string rawCode,
        int timeoutMs = 3000,
        string? pipeName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Invalid,
                "Informe uma leitura antes de enviar.");
        }

        if (rawCode.Length > ScannerSimulatorProtocol.MaximumRequestCharacters ||
            rawCode.Contains('\r') || rawCode.Contains('\n'))
        {
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Invalid,
                "A leitura contém quebra de linha ou excede o limite permitido.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                string.IsNullOrWhiteSpace(pipeName)
                    ? ScannerSimulatorProtocol.DefaultPipeName
                    : pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
            var request = new SimulatorScanRequest(rawCode);
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(request).AsMemory(),
                timeout.Token).ConfigureAwait(false);
            var responseLine = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SimulatorSubmissionResult>(responseLine ?? string.Empty)
                ?? new SimulatorSubmissionResult(
                    SimulatorSubmissionStatus.Error,
                    "O filtro retornou uma resposta vazia.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Unavailable,
                "Filtro não encontrado. Confirme que ele está em execução.");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Unavailable,
                $"Não foi possível comunicar com o filtro: {ex.Message}");
        }
    }
}
