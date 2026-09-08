using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PdvBarcodeFilter;

public interface IKeyboardSender
{
    void Send(string text, bool appendEnter, bool replaceCurrentField);
}

public sealed class KeyboardSender : IKeyboardSender
{
    public static int NativeInputStructureSize => Marshal.SizeOf<NativeMethods.Input>();

    public static int ExpectedNativeInputStructureSize => IntPtr.Size == 8 ? 40 : 28;

    public void Send(string text, bool appendEnter, bool replaceCurrentField = false)
    {
        if (NativeInputStructureSize != ExpectedNativeInputStructureSize)
        {
            throw new InvalidOperationException(
                $"Layout INPUT inválido: {NativeInputStructureSize} bytes; esperado {ExpectedNativeInputStructureSize}.");
        }

        var inputs = new List<NativeMethods.Input>(
            text.Length * 2 + (appendEnter ? 2 : 0) + (replaceCurrentField ? 4 : 0));

        if (replaceCurrentField)
        {
            // No modo de passagem livre, o conteúdo bruto já chegou ao campo.
            // Ctrl+A o seleciona antes de escrever somente o código tratado.
            inputs.Add(CreateVirtualKeyInput(NativeMethods.VkControl, keyUp: false));
            inputs.Add(CreateVirtualKeyInput(NativeMethods.VkA, keyUp: false));
            inputs.Add(CreateVirtualKeyInput(NativeMethods.VkA, keyUp: true));
            inputs.Add(CreateVirtualKeyInput(NativeMethods.VkControl, keyUp: true));
        }

        // KEYEVENTF_UNICODE independe do layout ativo do teclado do PDV.
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        if (appendEnter)
        {
            inputs.Add(CreateVirtualKeyInput(NativeMethods.VkReturn, keyUp: false));
            inputs.Add(CreateVirtualKeyInput(NativeMethods.VkReturn, keyUp: true));
        }

        if (inputs.Count == 0)
        {
            return;
        }

        var sent = NativeMethods.SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            NativeInputStructureSize);

        if (sent != inputs.Count)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SendInput enviou {sent} de {inputs.Count} eventos.");
        }
    }

    private static NativeMethods.Input CreateUnicodeInput(char character, bool keyUp) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Union = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeybdInput
            {
                ScanCode = character,
                Flags = NativeMethods.KeyeventfUnicode |
                        (keyUp ? NativeMethods.KeyeventfKeyup : 0)
            }
        }
    };

    private static NativeMethods.Input CreateVirtualKeyInput(int virtualKey, bool keyUp) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Union = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeybdInput
            {
                VirtualKey = (ushort)virtualKey,
                Flags = keyUp ? NativeMethods.KeyeventfKeyup : 0
            }
        }
    };
}
