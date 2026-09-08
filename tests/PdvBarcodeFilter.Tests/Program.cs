using PdvBarcodeFilter;
using System.Windows.Forms;

var tests = new (string Name, Action Run)[]
{
    ("1 - primeira leitura usa padrão", FirstScanUsesStandard),
    ("2 - repetição usa auxiliar", SecondScanUsesAuxiliary),
    ("3 - terceira tentativa permanece no auxiliar", ThirdScanKeepsAuxiliary),
    ("4 - novo cupom reinicia no padrão", NewCouponResetsToStandard),
    ("5 - segunda tentativa do novo cupom usa auxiliar", NewCouponSecondAttemptUsesAuxiliary),
    ("6 - leitura sem delimitadores é rejeitada", MalformedScanIsRejected),
    ("7 - campo padrão vazio é rejeitado", EmptyStandardIsRejected),
    ("8 - campo auxiliar vazio é rejeitado", EmptyAuxiliaryIsRejected),
    ("9 - montagem em alta velocidade não perde caracteres", HighSpeedAccumulationIsExact),
    ("10 - entrada injetada não é recapturada", InjectedInputIsIgnored),
    ("11 - leitura incompleta expira", IncompleteScanTimesOut),
    ("12 - leitura longa aciona proteção", OversizedScanIsBlocked),
    ("13 - flood aciona bloqueio temporário", ScannerFloodIsBlocked),
    ("14 - chave fechando funciona como delimitador", ClosingBraceDelimiterIsAccepted),
    ("15 - delimitadores mistos são aceitos", MixedDelimitersAreAccepted),
    ("16 - estrutura INPUT possui tamanho nativo correto", NativeInputLayoutIsCorrect),
    ("17 - entrada comum não parece cupom", OrdinaryKeyboardInputIsNotCoupon),
    ("18 - cupom válido é reconhecido antes do Enter", ValidCouponStructureIsRecognized),
    ("19 - atalho simples configurável é aceito", SimpleHotkeyIsAccepted),
    ("20 - atalho com modificadores é aceito", ModifiedHotkeyIsAccepted),
    ("21 - som de erro fica desligado por padrão", ErrorSoundIsDisabledByDefault),
    ("22 - log detalhado fica desligado por padrão", DetailedLoggingIsDisabledByDefault),
    ("23 - heartbeat padrão evita excesso de linhas", DefaultHeartbeatIsThrottled),
    ("24 - alternância antiga ainda pode ser configurada", LegacyAlternationCanBeConfigured),
    ("25 - simulador envia os códigos do cupom de exemplo", SimulatorSendsExampleCoupon),
    ("26 - retornar ao cupom anterior inicia nova sequência", ReturningCouponStartsNewSequence),
    ("extra - leitura inválida quebra a consecutividade", InvalidScanResetsSequence)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS | {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL | {test.Name} | {ex.Message}");
    }
}

Console.WriteLine($"\nResultado: {tests.Length - failures}/{tests.Length} testes aprovados.");
return failures == 0 ? 0 : 1;

static FilterSettings Defaults() => new();

static ScanProcessor NewProcessor() => new(Defaults());

static void FirstScanUsesStandard()
{
    var result = NewProcessor().Process(
        "11111111-2222-3333-4444-555555555555|STD01|AUX01");
    Equal(true, result.IsValid);
    Equal("STD01", result.Output);
    Equal(1, result.ConsecutiveCount);
}

static void SecondScanUsesAuxiliary()
{
    var processor = NewProcessor();
    const string raw = "11111111-2222-3333-4444-555555555555|STD01|AUX01";
    processor.Process(raw);
    var result = processor.Process(raw);
    Equal("AUX01", result.Output);
    Equal(2, result.ConsecutiveCount);
}

static void ThirdScanKeepsAuxiliary()
{
    var processor = NewProcessor();
    const string raw = "11111111-2222-3333-4444-555555555555|STD01|AUX01";
    processor.Process(raw);
    processor.Process(raw);
    var result = processor.Process(raw);
    Equal("AUX01", result.Output);
    Equal(3, result.ConsecutiveCount);
}

static void NewCouponResetsToStandard()
{
    var processor = NewProcessor();
    const string first = "11111111-2222-3333-4444-555555555555|STD01|AUX01";
    processor.Process(first);
    processor.Process(first);
    var result = processor.Process("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|AB123|ZZ999");
    Equal("AB123", result.Output);
    Equal(1, result.ConsecutiveCount);
}

static void NewCouponSecondAttemptUsesAuxiliary()
{
    var processor = NewProcessor();
    const string first = "11111111-2222-3333-4444-555555555555|STD01|AUX01";
    const string second = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee|AB123|ZZ999";
    processor.Process(first);
    processor.Process(first);
    processor.Process(second);
    var result = processor.Process(second);
    Equal("ZZ999", result.Output);
    Equal(2, result.ConsecutiveCount);
}

static void MalformedScanIsRejected()
{
    var result = NewProcessor().Process("qualquertexto");
    Equal(false, result.IsValid);
    Equal<string?>(null, result.Output);
}

static void EmptyStandardIsRejected()
{
    var result = NewProcessor().Process("UUID||AUX123");
    Equal(false, result.IsValid);
    Equal<string?>(null, result.Output);
}

static void EmptyAuxiliaryIsRejected()
{
    var result = NewProcessor().Process("UUID|ABC12|");
    Equal(false, result.IsValid);
    Equal<string?>(null, result.Output);
}

static void HighSpeedAccumulationIsExact()
{
    const string raw = "11111111-2222-3333-4444-555555555555|STD01|AUX01";
    var accumulator = new ScanAccumulator(4096);

    for (var repetition = 0; repetition < 10_000; repetition++)
    {
        foreach (var character in raw)
        {
            accumulator.Append(character.ToString());
        }

        var completed = accumulator.Complete();
        Equal(false, completed.Overflowed);
        Equal(raw, completed.RawCode);
    }
}

static void InjectedInputIsIgnored()
{
    Equal(true, KeyboardCapturePolicy.ShouldCapture(0));
    Equal(false, KeyboardCapturePolicy.ShouldCapture(KeyboardCapturePolicy.LlkhfInjected));
    Equal(false, KeyboardCapturePolicy.ShouldCapture(KeyboardCapturePolicy.LlkhfLowerIlInjected));
}

static void IncompleteScanTimesOut()
{
    var accumulator = new ScanAccumulator(4096);
    accumulator.Append("UUID|PAD");
    Equal(true, accumulator.IsTimedOut(750, Environment.TickCount64 + 1000));
    Equal(false, accumulator.IsTimedOut(750, Environment.TickCount64));
}

static void OversizedScanIsBlocked()
{
    var accumulator = new ScanAccumulator(32);
    accumulator.Append(new string('A', 33));
    var completed = accumulator.Complete();
    Equal(true, completed.Overflowed);
    Equal(string.Empty, completed.RawCode);
}

static void ScannerFloodIsBlocked()
{
    var settings = new FilterSettings
    {
        FloodWindowMs = 2000,
        MaxScansPerFloodWindow = 10,
        FloodCooldownMs = 5000
    };
    var guard = new ScannerFloodGuard(settings);
    const long start = 10_000;

    for (var index = 0; index < 10; index++)
    {
        Equal(FloodDecision.Allowed, guard.Evaluate(start + index));
    }

    Equal(FloodDecision.JustBlocked, guard.Evaluate(start + 10));
    Equal(FloodDecision.Blocked, guard.Evaluate(start + 100));
    Equal(FloodDecision.Allowed, guard.Evaluate(start + 6000));
}

static void ClosingBraceDelimiterIsAccepted()
{
    var processor = NewProcessor();
    const string raw = "99999999-8888-7777-6666-555555555555}STD02}AUX02";
    Equal("STD02", processor.Process(raw).Output);
    Equal("AUX02", processor.Process(raw).Output);
    Equal("AUX02", processor.Process(raw).Output);
}

static void MixedDelimitersAreAccepted()
{
    var processor = NewProcessor();
    var result = processor.Process("UUID|PADRAO}AUXILIAR");
    Equal(true, result.IsValid);
    Equal("PADRAO", result.Output);
}

static void NativeInputLayoutIsCorrect()
{
    Equal(KeyboardSender.ExpectedNativeInputStructureSize, KeyboardSender.NativeInputStructureSize);
    Equal(IntPtr.Size == 8 ? 40 : 28, KeyboardSender.NativeInputStructureSize);
}

static void OrdinaryKeyboardInputIsNotCoupon()
{
    Equal(false, NewProcessor().HasValidStructure("texto digitado normalmente"));
    Equal(false, NewProcessor().HasValidStructure("copiar|colar"));
}

static void ValidCouponStructureIsRecognized()
{
    Equal(true, NewProcessor().HasValidStructure("UUID|PADRAO|AUX"));
    Equal(true, NewProcessor().HasValidStructure("UUID}PADRAO}AUX"));
}

static void SimpleHotkeyIsAccepted()
{
    var gesture = HotkeyGesture.Parse("F12");
    Equal((uint)Keys.F12, gesture.VirtualKey);
    Equal("F12", gesture.DisplayText);
}

static void ModifiedHotkeyIsAccepted()
{
    var gesture = HotkeyGesture.Parse("Ctrl+Alt+P");
    Equal((uint)Keys.P, gesture.VirtualKey);
    Equal("Ctrl+Alt+P", gesture.DisplayText);
}

static void ErrorSoundIsDisabledByDefault()
{
    Equal(false, Defaults().BeepOnError);
}

static void DetailedLoggingIsDisabledByDefault()
{
    Equal(false, Defaults().DetailedInputLogging);
}

static void DefaultHeartbeatIsThrottled()
{
    Equal(30_000, Defaults().DiagnosticHeartbeatMs);
}

static void LegacyAlternationCanBeConfigured()
{
    var processor = new ScanProcessor(new FilterSettings
    {
        UseAuxiliaryFromSecondScan = false,
        AlternateRepeatedScans = true
    });
    const string raw = "UUID|PADRAO|AUXILIAR";
    Equal("PADRAO", processor.Process(raw).Output);
    Equal("AUXILIAR", processor.Process(raw).Output);
    Equal("PADRAO", processor.Process(raw).Output);
}

static void SimulatorSendsExampleCoupon()
{
    const string raw = "00000000-0000-0000-0000-000000000001}X96UG}2W7LX1";
    var pipeName = $"PdvBarcodeFilter.Tests.{Guid.NewGuid():N}";
    using var sender = new RecordingKeyboardSender();
    var settings = new FilterSettings
    {
        RealtimeDiagnosticsEnabled = false,
        ShowConfirmations = false,
        SendEnter = false
    };
    using var diagnostics = new RealtimeDiagnosticHub(settings);
    using var coordinator = new ScanCoordinator(settings, sender, _ => { }, diagnostics);
    using var server = new ScannerSimulatorServer(
        code => coordinator.TryPost(new CapturedScan(
            code,
            Overflowed: false,
            TimedOut: false,
            ReplaceCurrentField: true))
            ? SimulatorSubmissionResult.Accepted()
            : new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Invalid,
                "Leitura rejeitada."),
        pipeName);

    var firstResponse = ScannerSimulatorClient.SendAsync(raw, pipeName: pipeName)
        .GetAwaiter().GetResult();
    Equal(SimulatorSubmissionStatus.Accepted, firstResponse.Status);
    Equal("X96UG", sender.WaitForNext());

    var secondResponse = ScannerSimulatorClient.SendAsync(raw, pipeName: pipeName)
        .GetAwaiter().GetResult();
    Equal(SimulatorSubmissionStatus.Accepted, secondResponse.Status);
    Equal("2W7LX1", sender.WaitForNext());

    var thirdResponse = ScannerSimulatorClient.SendAsync(raw, pipeName: pipeName)
        .GetAwaiter().GetResult();
    Equal(SimulatorSubmissionStatus.Accepted, thirdResponse.Status);
    Equal("2W7LX1", sender.WaitForNext());
}

static void ReturningCouponStartsNewSequence()
{
    var processor = NewProcessor();
    const string first = "UUID-A|PADRAO-A|AUXILIAR-A";
    const string second = "UUID-B|PADRAO-B|AUXILIAR-B";

    Equal("PADRAO-A", processor.Process(first).Output);
    Equal("AUXILIAR-A", processor.Process(first).Output);
    Equal("PADRAO-B", processor.Process(second).Output);
    Equal("PADRAO-A", processor.Process(first).Output);
}

static void InvalidScanResetsSequence()
{
    var processor = NewProcessor();
    const string raw = "UUID|PADRAO|AUX";
    Equal("PADRAO", processor.Process(raw).Output);
    Equal("AUX", processor.Process(raw).Output);
    Equal(false, processor.Process("defeituoso").IsValid);
    Equal("PADRAO", processor.Process(raw).Output);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Esperado: {expected ?? (object)"<null>"}; recebido: {actual ?? (object)"<null>"}.");
    }
}

sealed class RecordingKeyboardSender : IKeyboardSender, IDisposable
{
    private readonly Queue<string> _values = new();
    private readonly AutoResetEvent _received = new(false);
    private readonly object _sync = new();

    public void Send(string text, bool appendEnter, bool replaceCurrentField)
    {
        lock (_sync)
        {
            _values.Enqueue(text);
        }

        _received.Set();
    }

    public string WaitForNext()
    {
        if (!_received.WaitOne(TimeSpan.FromSeconds(3)))
        {
            throw new TimeoutException("O código tratado não chegou ao emissor de teste.");
        }

        lock (_sync)
        {
            return _values.Dequeue();
        }
    }

    public void Dispose() => _received.Dispose();
}
