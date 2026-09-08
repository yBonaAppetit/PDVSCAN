using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PdvBarcodeFilter;

public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public static HotkeyGesture Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("ToggleHotkey não pode ficar vazio.");
        }

        uint modifiers = NativeMethods.ModNoRepeat;
        Keys key = Keys.None;
        var displayParts = new List<string>();

        foreach (var rawPart in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= NativeMethods.ModControl;
                    displayParts.Add("Ctrl");
                    continue;
                case "ALT":
                    modifiers |= NativeMethods.ModAlt;
                    displayParts.Add("Alt");
                    continue;
                case "SHIFT":
                    modifiers |= NativeMethods.ModShift;
                    displayParts.Add("Shift");
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= NativeMethods.ModWin;
                    displayParts.Add("Win");
                    continue;
            }

            if (key != Keys.None ||
                !Enum.TryParse(rawPart, ignoreCase: true, out key) ||
                key == Keys.None ||
                key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin)
            {
                throw new InvalidDataException(
                    "ToggleHotkey inválido. Exemplos aceitos: F12, Ctrl+F12 ou Ctrl+Alt+P.");
            }

            key &= Keys.KeyCode;
            displayParts.Add(key.ToString());
        }

        if (key == Keys.None)
        {
            throw new InvalidDataException("ToggleHotkey precisa possuir uma tecla principal.");
        }

        return new HotkeyGesture(modifiers, (uint)key, string.Join('+', displayParts));
    }
}

public sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x5044;
    private readonly Action _pressed;
    private readonly RealtimeDiagnosticHub _diagnostics;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkey(
        HotkeyGesture gesture,
        Action pressed,
        RealtimeDiagnosticHub diagnostics)
    {
        Gesture = gesture;
        _pressed = pressed;
        _diagnostics = diagnostics;

        CreateHandle(new CreateParams
        {
            Caption = "PdvBarcodeFilter.GlobalHotkey",
            Parent = new nint(-3)
        });

        if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, gesture.Modifiers, gesture.VirtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            DestroyHandle();
            throw new InvalidOperationException(
                $"Não foi possível registrar o atalho {gesture.DisplayText}: {error.Message}",
                error);
        }

        _registered = true;
        diagnostics.Write("HOTKEY_REGISTER", $"OK | gesture={gesture.DisplayText}");
    }

    public HotkeyGesture Gesture { get; }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == HotkeyId)
        {
            _diagnostics.Write("HOTKEY_PRESSED", $"gesture={Gesture.DisplayText}");
            _pressed();
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
            _diagnostics.Write("HOTKEY_UNREGISTER", $"gesture={Gesture.DisplayText}");
        }

        DestroyHandle();
    }
}
