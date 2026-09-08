namespace PdvBarcodeFilter;

public enum FilterNotificationKind
{
    StandardSent,
    AuxiliarySent,
    InvalidScan,
    ScannerBlocked,
    HookRecovered,
    Error
}

public sealed record FilterNotification(FilterNotificationKind Kind, string Message);
