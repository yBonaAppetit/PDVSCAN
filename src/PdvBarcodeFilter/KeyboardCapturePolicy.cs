namespace PdvBarcodeFilter;

public static class KeyboardCapturePolicy
{
    public const uint LlkhfLowerIlInjected = 0x00000002;
    public const uint LlkhfInjected = 0x00000010;

    public static bool ShouldCapture(uint flags) =>
        (flags & (LlkhfInjected | LlkhfLowerIlInjected)) == 0;
}
