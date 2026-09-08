using System.Text.Json;

namespace PdvBarcodeFilter;

public sealed class FilterSettings
{
    public string Delimiter { get; init; } = "|";
    public string AlternateDelimiters { get; init; } = "}";
    public bool AlternateRepeatedScans { get; init; } = true;
    public bool UseAuxiliaryFromSecondScan { get; init; } = true;
    public bool SendEnter { get; init; } = true;
    public bool BeepOnError { get; init; }
    public bool StartPaused { get; init; }
    public bool KeyboardPassthrough { get; init; } = true;
    public string ToggleHotkey { get; init; } = "Ctrl+Shift+F12";
    public int MaxScanLength { get; init; } = 4096;
    public int InterCharacterTimeoutMs { get; init; } = 750;
    public int WatchdogIntervalMs { get; init; } = 60000;
    public int FloodWindowMs { get; init; } = 2000;
    public int MaxScansPerFloodWindow { get; init; } = 10;
    public int FloodCooldownMs { get; init; } = 5000;
    public bool ShowConfirmations { get; init; } = true;
    public bool RealtimeDiagnosticsEnabled { get; init; } = true;
    public bool DiagnosticLogRawData { get; init; } = true;
    public bool DetailedInputLogging { get; init; }
    public bool SimulatorInputEnabled { get; init; }
    public int DiagnosticHeartbeatMs { get; init; } = 30000;
    public int DiagnosticMaxFileMb { get; init; } = 25;

    public char GetDelimiter()
    {
        if (Delimiter.Length != 1)
        {
            throw new InvalidDataException("Delimiter deve possuir exatamente um caractere.");
        }

        return Delimiter[0];
    }

    public char[] GetAcceptedDelimiters()
    {
        var delimiters = (Delimiter + AlternateDelimiters)
            .Where(character => !char.IsControl(character))
            .Distinct()
            .ToArray();

        if (delimiters.Length == 0)
        {
            throw new InvalidDataException("Ao menos um delimitador deve ser configurado.");
        }

        return delimiters;
    }

    public void Validate()
    {
        _ = GetDelimiter();
        _ = GetAcceptedDelimiters();
        if (MaxScanLength is < 32 or > 65536)
        {
            throw new InvalidDataException("MaxScanLength deve estar entre 32 e 65536.");
        }

        if (InterCharacterTimeoutMs is < 100 or > 10000)
        {
            throw new InvalidDataException("InterCharacterTimeoutMs deve estar entre 100 e 10000.");
        }

        if (WatchdogIntervalMs is < 10000 or > 600000)
        {
            throw new InvalidDataException("WatchdogIntervalMs deve estar entre 10000 e 600000.");
        }

        if (FloodWindowMs is < 250 or > 60000 ||
            MaxScansPerFloodWindow is < 2 or > 1000 ||
            FloodCooldownMs is < 500 or > 300000)
        {
            throw new InvalidDataException("As configurações de proteção contra flood estão fora dos limites.");
        }

        if (DiagnosticHeartbeatMs is < 1000 or > 60000 ||
            DiagnosticMaxFileMb is < 1 or > 1024)
        {
            throw new InvalidDataException("As configurações do diagnóstico estão fora dos limites.");
        }

        _ = HotkeyGesture.Parse(ToggleHotkey);
    }
}

public sealed record SettingsLoadResult(FilterSettings Settings, Exception? Error);

public static class SettingsLoader
{
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "filtersettings.json");

    public static SettingsLoadResult Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = new FilterSettings();
                defaults.Validate();
                return new SettingsLoadResult(defaults, null);
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<FilterSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidDataException("O arquivo de configuração está vazio.");

            settings.Validate();
            return new SettingsLoadResult(settings, null);
        }
        catch (Exception ex)
        {
            return new SettingsLoadResult(new FilterSettings(), ex);
        }
    }
}
