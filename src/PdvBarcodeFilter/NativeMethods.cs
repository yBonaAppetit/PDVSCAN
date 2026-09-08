using System.Runtime.InteropServices;
using System.Text;

namespace PdvBarcodeFilter;

internal static class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmSysKeyUp = 0x0105;
    internal const int WmInputDeviceChange = 0x00FE;
    internal const int WmInput = 0x00FF;
    internal const int WmHotkey = 0x0312;
    internal const int VkBack = 0x08;
    internal const int VkReturn = 0x0D;
    internal const int VkShift = 0x10;
    internal const int VkControl = 0x11;
    internal const int VkA = 0x41;
    internal const int VkMenu = 0x12;
    internal const int VkLShift = 0xA0;
    internal const int VkRShift = 0xA1;
    internal const int VkLControl = 0xA2;
    internal const int VkRControl = 0xA3;
    internal const int VkLMenu = 0xA4;
    internal const int VkRMenu = 0xA5;
    internal const uint InputKeyboard = 1;
    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint KeyeventfUnicode = 0x0004;
    internal const uint RidevInputSink = 0x00000100;
    internal const uint RidevDevNotify = 0x00002000;
    internal const uint RidInput = 0x10000003;
    internal const uint RidiDeviceName = 0x20000007;
    internal const uint RimTypeMouse = 0;
    internal const uint RimTypeKeyboard = 1;
    internal const uint RimTypeHid = 2;
    internal const long GidcArrival = 1;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint ModNoRepeat = 0x4000;

    internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        internal uint VkCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal MouseInput Mouse;

        [FieldOffset(0)]
        internal KeybdInput Keyboard;

        [FieldOffset(0)]
        internal HardwareInput Hardware;
    }

    // INPUT contém uma união. Mesmo usando apenas teclado, a união precisa
    // manter o tamanho do maior membro nativo (MOUSEINPUT: 32 bytes em x64).
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeybdInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        internal uint Message;
        internal ushort ParameterLow;
        internal ushort ParameterHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDevice
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDeviceList
    {
        internal nint Device;
        internal uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputHeader
    {
        internal uint Type;
        internal uint Size;
        internal nint Device;
        internal nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawKeyboard
    {
        internal ushort MakeCode;
        internal ushort Flags;
        internal ushort Reserved;
        internal ushort VirtualKey;
        internal uint Message;
        internal uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawHidHeader
    {
        internal uint SizeHid;
        internal uint Count;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookExW(
        int hookId,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        byte[] keyboardState,
        [Out] StringBuilder buffer,
        int bufferSize,
        uint flags,
        nint keyboardLayout);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint structureSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceList[]? devices,
        ref uint deviceCount,
        uint structureSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern uint GetRawInputDeviceInfoW(
        nint device,
        uint command,
        StringBuilder? data,
        ref uint dataSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        nint rawInput,
        uint command,
        nint data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);
}
