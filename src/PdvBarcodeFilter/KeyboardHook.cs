using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PdvBarcodeFilter;

public sealed class KeyboardHook : IDisposable
{
    private readonly Func<CapturedScan, bool> _onScan;
    private readonly ScanAccumulator _accumulator;
    private readonly byte[] _keyboardState = new byte[256];
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly RealtimeDiagnosticHub _diagnostics;
    private readonly bool _keyboardPassthrough;
    private nint _hook;
    private bool _paused;
    private bool _disposed;
    private long _physicalKeyDowns;
    private long _injectedKeyDowns;
    private long _completedScans;
    private bool _suppressNextEnterKeyUp;

    public KeyboardHook(
        int maxScanLength,
        Func<CapturedScan, bool> onScan,
        RealtimeDiagnosticHub diagnostics,
        bool keyboardPassthrough)
    {
        _onScan = onScan;
        _diagnostics = diagnostics;
        _keyboardPassthrough = keyboardPassthrough;
        _accumulator = new ScanAccumulator(maxScanLength);
        _callback = HookCallback;
        InstallHook();
    }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            _accumulator.Reset();
            Array.Clear(_keyboardState);
            _suppressNextEnterKeyUp = false;
            _diagnostics.Write(
                "HOOK_STATE",
                value
                    ? "PAUSADO; teclado e scanner serão liberados sem tratamento."
                    : _keyboardPassthrough
                        ? "ATIVO_COM_TECLADO_LIVRE; somente o ENTER de um cupom válido será suprimido."
                        : "ATIVO_BLOQUEANTE; toda entrada física será suprimida.");
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
        var message = unchecked((int)wParam.ToInt64());
        var isKeyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var isKeyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

        // SendInput marca os eventos gerados por este programa como injetados.
        // Eles devem chegar ao PDV e nunca podem voltar ao acumulador.
        if (!KeyboardCapturePolicy.ShouldCapture(data.Flags))
        {
            if (isKeyDown)
            {
                Interlocked.Increment(ref _injectedKeyDowns);
                if (_diagnostics.DetailedInputLogging)
                {
                    _diagnostics.Write(
                        "HOOK_INJECTED",
                        $"LIBERADO | vk=0x{data.VkCode:X2} scan=0x{data.ScanCode:X2} flags=0x{data.Flags:X2}");
                }
            }

            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (isKeyDown)
        {
            Interlocked.Increment(ref _physicalKeyDowns);
        }

        if (_paused)
        {
            if (isKeyDown && _diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write(
                    "HOOK_PHYSICAL",
                    $"LIBERADO_PAUSADO | vk=0x{data.VkCode:X2} scan=0x{data.ScanCode:X2} flags=0x{data.Flags:X2}");
            }

            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var suppressThisEvent = false;
        try
        {
            if (isKeyDown)
            {
                if (_diagnostics.DetailedInputLogging)
                {
                    _diagnostics.Write(
                        "HOOK_PHYSICAL",
                        $"{(_keyboardPassthrough ? "OBSERVADO_E_LIBERADO" : "CAPTURADO_E_BLOQUEADO")} | " +
                        $"vk=0x{data.VkCode:X2} scan=0x{data.ScanCode:X2} flags=0x{data.Flags:X2} buffer_antes={_accumulator.PendingLength}");
                }
                UpdateKeyState(data.VkCode, true);
                suppressThisEvent = HandleKeyDown(data);
            }
            else if (isKeyUp)
            {
                UpdateKeyState(data.VkCode, false);
                if (_keyboardPassthrough &&
                    data.VkCode == NativeMethods.VkReturn &&
                    _suppressNextEnterKeyUp)
                {
                    _suppressNextEnterKeyUp = false;
                    suppressThisEvent = true;
                    if (_diagnostics.DetailedInputLogging)
                    {
                        _diagnostics.Write("SCAN_INTERCEPT", "Key-up do ENTER original suprimido.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Falha fechada: uma exceção não deve deixar o código bruto vazar.
            _accumulator.Reset();
            _diagnostics.Write("HOOK_ERROR", $"{ex.GetType().Name}: {ex.Message}; buffer descartado.");
        }

        if (_keyboardPassthrough && !suppressThisEvent)
        {
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // Retorno diferente de zero suprime o evento físico selecionado.
        return 1;
    }

    private bool HandleKeyDown(NativeMethods.KbdLlHookStruct data)
    {
        if (data.VkCode == NativeMethods.VkReturn)
        {
            var scan = _accumulator.Complete() with
            {
                ReplaceCurrentField = _keyboardPassthrough
            };
            var accepted = _onScan(scan);
            if (accepted)
            {
                Interlocked.Increment(ref _completedScans);
                _suppressNextEnterKeyUp = _keyboardPassthrough;
                _diagnostics.Write(
                    "SCAN_COMPLETE",
                    $"ENTER de cupom interceptado | length={scan.RawCode.Length} overflow={scan.Overflowed} " +
                    $"replace_field={scan.ReplaceCurrentField} raw={_diagnostics.DisplayValue(scan.RawCode)}");
            }
            else if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write(
                    "KEYBOARD_ENTER",
                    $"ENTER comum liberado | buffer_descartado={scan.RawCode.Length}");
            }

            return accepted;
        }

        if (data.VkCode == NativeMethods.VkBack)
        {
            _accumulator.Backspace();
            if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write("HOOK_TRANSLATE", $"BACKSPACE | buffer_agora={_accumulator.PendingLength}");
            }
            return false;
        }

        if (IsModifier(data.VkCode))
        {
            return false;
        }

        var translated = new StringBuilder(8);
        const uint doNotChangeKeyboardState = 0x0004;
        var characterCount = NativeMethods.ToUnicodeEx(
            data.VkCode,
            data.ScanCode,
            _keyboardState,
            translated,
            translated.Capacity,
            doNotChangeKeyboardState,
            NativeMethods.GetKeyboardLayout(0));

        if (characterCount > 0)
        {
            var text = translated.ToString(0, characterCount);
            _accumulator.Append(text);
            if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write(
                    "HOOK_TRANSLATE",
                    $"vk=0x{data.VkCode:X2} -> {_diagnostics.DisplayValue(text)} | buffer_agora={_accumulator.PendingLength}");
            }
        }
        else if (_diagnostics.DetailedInputLogging)
        {
            _diagnostics.Write(
                "HOOK_TRANSLATE",
                $"vk=0x{data.VkCode:X2} sem caractere | ToUnicodeEx={characterCount}");
        }

        return false;
    }

    private void UpdateKeyState(uint virtualKey, bool isDown)
    {
        if (virtualKey < _keyboardState.Length)
        {
            _keyboardState[virtualKey] = isDown ? (byte)0x80 : (byte)0;
        }

        var genericKey = virtualKey switch
        {
            NativeMethods.VkLShift or NativeMethods.VkRShift => NativeMethods.VkShift,
            NativeMethods.VkLControl or NativeMethods.VkRControl => NativeMethods.VkControl,
            NativeMethods.VkLMenu or NativeMethods.VkRMenu => NativeMethods.VkMenu,
            _ => 0
        };

        if (genericKey != 0)
        {
            _keyboardState[genericKey] = isDown ? (byte)0x80 : (byte)0;
        }
    }

    private static bool IsModifier(uint virtualKey) => virtualKey is
        NativeMethods.VkShift or NativeMethods.VkControl or NativeMethods.VkMenu or
        NativeMethods.VkLShift or NativeMethods.VkRShift or
        NativeMethods.VkLControl or NativeMethods.VkRControl or
        NativeMethods.VkLMenu or NativeMethods.VkRMenu;

    public bool CheckForTimedOutScan(int timeoutMs)
    {
        if (_paused || !_accumulator.IsTimedOut(timeoutMs, Environment.TickCount64))
        {
            return false;
        }

        var pending = _accumulator.Complete();
        if (_keyboardPassthrough)
        {
            if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write(
                    "KEYBOARD_TIMEOUT",
                    $"Buffer de entrada comum limpo | length={pending.RawCode.Length} overflow={pending.Overflowed}");
            }

            return true;
        }

        _diagnostics.Write(
            "SCAN_TIMEOUT",
            $"Leitura descartada | overflow={pending.Overflowed} raw={_diagnostics.DisplayValue(pending.RawCode)}");

        _onScan(pending.Overflowed
            ? pending
            : new CapturedScan(string.Empty, Overflowed: false, TimedOut: true));
        return true;
    }

    public bool RefreshHookIfIdle()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KeyboardHook));
        }

        // Nunca interrompe uma leitura que já começou. Fora de uma leitura,
        // recadastrar o hook é uma operação curta e não depende de sondas
        // injetadas, que variam de comportamento entre ambientes Windows.
        if (_accumulator.HasPendingInput)
        {
            if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write("HOOK_REFRESH", "Adiado: existe leitura em andamento.");
            }

            return false;
        }

        ReinstallHook();
        if (_diagnostics.DetailedInputLogging)
        {
            _diagnostics.Write("HOOK_REFRESH", $"Recadastrado silenciosamente | handle=0x{_hook.ToInt64():X}");
        }
        return true;
    }

    public KeyboardHookStatistics GetStatistics() => new(
        Interlocked.Read(ref _physicalKeyDowns),
        Interlocked.Read(ref _injectedKeyDowns),
        Interlocked.Read(ref _completedScans),
        _accumulator.PendingLength,
        _paused,
        _keyboardPassthrough);

    private void ReinstallHook()
    {
        var replacement = CreateHook(logSuccess: _diagnostics.DetailedInputLogging);
        var previous = _hook;
        _hook = replacement;

        if (previous != 0)
        {
            NativeMethods.UnhookWindowsHookEx(previous);
        }

        _accumulator.Reset();
        Array.Clear(_keyboardState);
    }

    private void InstallHook()
    {
        _hook = CreateHook(logSuccess: true);
    }

    private nint CreateHook(bool logSuccess)
    {
        var module = NativeMethods.GetModuleHandleW(null);
        var hook = NativeMethods.SetWindowsHookExW(
            NativeMethods.WhKeyboardLl,
            _callback,
            module,
            0);

        if (hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Não foi possível instalar o hook global de teclado.");
        }

        if (logSuccess)
        {
            _diagnostics.Write("HOOK_INSTALL", $"OK | handle=0x{hook.ToInt64():X}");
        }
        return hook;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _diagnostics.Write("HOOK_DISPOSE", $"Removido | handle=0x{_hook.ToInt64():X}");
            _hook = 0;
        }
    }
}

public sealed record KeyboardHookStatistics(
    long PhysicalKeyDowns,
    long InjectedKeyDowns,
    long CompletedScans,
    int PendingCharacters,
    bool Paused,
    bool KeyboardPassthrough);
