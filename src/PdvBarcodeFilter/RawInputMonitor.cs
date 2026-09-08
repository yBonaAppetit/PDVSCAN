using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PdvBarcodeFilter;

public sealed class RawInputMonitor : NativeWindow, IDisposable
{
    private readonly RealtimeDiagnosticHub _diagnostics;
    private readonly Dictionary<nint, string> _deviceNames = new();
    private bool _disposed;
    private long _keyboardEvents;
    private long _hidReports;

    public RawInputMonitor(RealtimeDiagnosticHub diagnostics)
    {
        _diagnostics = diagnostics;
        CreateHandle(new CreateParams
        {
            Caption = "PdvBarcodeFilter.RawInputMonitor",
            Parent = new nint(-3) // HWND_MESSAGE
        });

        RegisterDevice(0x01, 0x06, "teclado HID");
        RegisterDevice(0x8C, 0x02, "scanner POS HID");
        WriteDeviceInventory();
    }

    public RawInputStatistics GetStatistics() => new(
        Interlocked.Read(ref _keyboardEvents),
        Interlocked.Read(ref _hidReports));

    private void RegisterDevice(ushort usagePage, ushort usage, string description)
    {
        var device = new[]
        {
            new NativeMethods.RawInputDevice
            {
                UsagePage = usagePage,
                Usage = usage,
                Flags = NativeMethods.RidevInputSink | NativeMethods.RidevDevNotify,
                Target = Handle
            }
        };

        if (NativeMethods.RegisterRawInputDevices(
                device,
                (uint)device.Length,
                (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>()))
        {
            _diagnostics.Write(
                "RAW_REGISTER",
                $"OK | {description} | usage_page=0x{usagePage:X2} usage=0x{usage:X2}");
        }
        else
        {
            _diagnostics.Write(
                "RAW_REGISTER",
                $"ERRO | {description} | usage_page=0x{usagePage:X2} usage=0x{usage:X2} win32={Marshal.GetLastWin32Error()}");
        }
    }

    private void WriteDeviceInventory()
    {
        uint count = 0;
        var structureSize = (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceList>();
        var firstResult = NativeMethods.GetRawInputDeviceList(null, ref count, structureSize);
        if (firstResult == uint.MaxValue)
        {
            _diagnostics.Write("RAW_INVENTORY", $"Falha ao contar dispositivos | win32={Marshal.GetLastWin32Error()}");
            return;
        }

        if (count == 0)
        {
            _diagnostics.Write("RAW_INVENTORY", "Nenhum dispositivo Raw Input encontrado.");
            return;
        }

        var devices = new NativeMethods.RawInputDeviceList[count];
        var result = NativeMethods.GetRawInputDeviceList(devices, ref count, structureSize);
        if (result == uint.MaxValue)
        {
            _diagnostics.Write("RAW_INVENTORY", $"Falha ao enumerar dispositivos | win32={Marshal.GetLastWin32Error()}");
            return;
        }

        _diagnostics.Write("RAW_INVENTORY", $"Quantidade={count}");
        if (_diagnostics.DetailedInputLogging)
        {
            for (var index = 0; index < count; index++)
            {
                var item = devices[index];
                _diagnostics.Write(
                    "RAW_DEVICE",
                    $"index={index} type={TypeName(item.Type)} handle=0x{item.Device.ToInt64():X} path={GetDeviceName(item.Device)}");
            }
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmInput)
        {
            ProcessRawInput(message.LParam);
        }
        else if (message.Msg == NativeMethods.WmInputDeviceChange)
        {
            if (_diagnostics.DetailedInputLogging)
            {
                var change = message.WParam.ToInt64() == NativeMethods.GidcArrival ? "CONECTADO" : "REMOVIDO";
                _diagnostics.Write(
                    "RAW_DEVICE_CHANGE",
                    $"{change} | handle=0x{message.LParam.ToInt64():X} | path={GetDeviceName(message.LParam)}");
            }
        }

        base.WndProc(ref message);
    }

    private void ProcessRawInput(nint rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RawInputHeader>();
        if (NativeMethods.GetRawInputData(
                rawInputHandle,
                NativeMethods.RidInput,
                0,
                ref size,
                headerSize) == uint.MaxValue || size < headerSize)
        {
            _diagnostics.Write("RAW_INPUT_ERROR", $"Falha ao obter tamanho | win32={Marshal.GetLastWin32Error()}");
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var copied = NativeMethods.GetRawInputData(
                rawInputHandle,
                NativeMethods.RidInput,
                buffer,
                ref size,
                headerSize);
            if (copied == uint.MaxValue)
            {
                _diagnostics.Write("RAW_INPUT_ERROR", $"Falha ao ler relatório | win32={Marshal.GetLastWin32Error()}");
                return;
            }

            var header = Marshal.PtrToStructure<NativeMethods.RawInputHeader>(buffer);
            var data = nint.Add(buffer, Marshal.SizeOf<NativeMethods.RawInputHeader>());

            if (header.Type == NativeMethods.RimTypeKeyboard)
            {
                var keyboard = Marshal.PtrToStructure<NativeMethods.RawKeyboard>(data);
                Interlocked.Increment(ref _keyboardEvents);
                if (_diagnostics.DetailedInputLogging)
                {
                    _diagnostics.Write(
                        "RAW_KEYBOARD",
                        $"device={GetDeviceName(header.Device)} make=0x{keyboard.MakeCode:X2} flags=0x{keyboard.Flags:X2} vkey=0x{keyboard.VirtualKey:X2} message=0x{keyboard.Message:X4}");
                }
            }
            else if (header.Type == NativeMethods.RimTypeHid)
            {
                var hid = Marshal.PtrToStructure<NativeMethods.RawHidHeader>(data);
                Interlocked.Increment(ref _hidReports);
                if (_diagnostics.DetailedInputLogging)
                {
                    var totalBytes = checked((int)Math.Min(hid.SizeHid * hid.Count, 4096u));
                    var payload = new byte[totalBytes];
                    Marshal.Copy(nint.Add(data, Marshal.SizeOf<NativeMethods.RawHidHeader>()), payload, 0, totalBytes);
                    _diagnostics.Write(
                        "RAW_HID",
                        $"device={GetDeviceName(header.Device)} report_size={hid.SizeHid} count={hid.Count} bytes={Convert.ToHexString(payload)}");
                }
            }
            else if (_diagnostics.DetailedInputLogging)
            {
                _diagnostics.Write(
                    "RAW_OTHER",
                    $"type={header.Type} device={GetDeviceName(header.Device)} size={header.Size}");
            }
        }
        catch (Exception ex)
        {
            _diagnostics.Write("RAW_INPUT_ERROR", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string GetDeviceName(nint device)
    {
        if (device == 0)
        {
            return "<sem-dispositivo>";
        }

        if (_deviceNames.TryGetValue(device, out var cached))
        {
            return cached;
        }

        uint characterCount = 0;
        NativeMethods.GetRawInputDeviceInfoW(
            device,
            NativeMethods.RidiDeviceName,
            null,
            ref characterCount);
        if (characterCount == 0)
        {
            return $"<desconhecido:0x{device.ToInt64():X}>";
        }

        var name = new StringBuilder((int)characterCount + 1);
        var result = NativeMethods.GetRawInputDeviceInfoW(
            device,
            NativeMethods.RidiDeviceName,
            name,
            ref characterCount);
        var value = result == uint.MaxValue
            ? $"<erro:{Marshal.GetLastWin32Error()}:0x{device.ToInt64():X}>"
            : name.ToString();
        _deviceNames[device] = value;
        return value;
    }

    private static string TypeName(uint type) => type switch
    {
        NativeMethods.RimTypeMouse => "MOUSE",
        NativeMethods.RimTypeKeyboard => "KEYBOARD",
        NativeMethods.RimTypeHid => "HID",
        _ => $"UNKNOWN({type})"
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyHandle();
    }
}

public sealed record RawInputStatistics(long KeyboardEvents, long HidReports);
