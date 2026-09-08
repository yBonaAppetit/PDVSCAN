namespace PdvBarcodeFilter;

public sealed record ScanResult(
    bool IsValid,
    string? Output,
    bool IsRepeated,
    bool UsedAuxiliary,
    int ConsecutiveCount,
    string? Error);

public sealed class ScanProcessor
{
    private readonly char[] _delimiters;
    private readonly bool _alternateRepeatedScans;
    private readonly bool _useAuxiliaryFromSecondScan;
    private readonly object _sync = new();
    private string? _lastRawCode;
    private int _consecutiveCount;

    public ScanProcessor(FilterSettings settings)
    {
        _delimiters = settings.GetAcceptedDelimiters();
        _alternateRepeatedScans = settings.AlternateRepeatedScans;
        _useAuxiliaryFromSecondScan = settings.UseAuxiliaryFromSecondScan;
    }

    public ScanResult Process(string rawCode)
    {
        lock (_sync)
        {
            var fields = rawCode.Split(_delimiters, StringSplitOptions.None);
            if (!HasValidFields(fields))
            {
                Reset();
                return new ScanResult(false, null, false, false, 0,
                    "A leitura deve conter identificador, código padrão e código auxiliar não vazios.");
            }

            var isRepeated = string.Equals(rawCode, _lastRawCode, StringComparison.Ordinal);
            if (isRepeated)
            {
                _consecutiveCount++;
            }
            else
            {
                _lastRawCode = rawCode;
                _consecutiveCount = 1;
            }

            var useAuxiliary = _useAuxiliaryFromSecondScan
                ? _consecutiveCount >= 2
                : _alternateRepeatedScans && _consecutiveCount % 2 == 0;
            var output = (useAuxiliary ? fields[2] : fields[1]).Trim();

            return new ScanResult(true, output, isRepeated, useAuxiliary, _consecutiveCount, null);
        }
    }

    public bool HasValidStructure(string rawCode) =>
        HasValidFields(rawCode.Split(_delimiters, StringSplitOptions.None));

    private static bool HasValidFields(string[] fields) =>
        fields.Length >= 3 &&
        !string.IsNullOrWhiteSpace(fields[1]) &&
        !string.IsNullOrWhiteSpace(fields[2]);

    private void Reset()
    {
        _lastRawCode = null;
        _consecutiveCount = 0;
    }
}
