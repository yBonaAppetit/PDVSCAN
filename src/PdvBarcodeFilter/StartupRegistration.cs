using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace PdvBarcodeFilter;

public static class StartupRegistration
{
    public const string ScheduledTaskName = "PdvBarcodeFilter";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "PdvBarcodeFilter";

    public static bool IsEnabled()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = GetSchtasksPath(),
            Arguments = $"/Query /TN \"{ScheduledTaskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null)
        {
            return false;
        }

        process.WaitForExit(5000);
        return process.HasExited && process.ExitCode == 0;
    }

    public static void SetEnabled(bool enabled)
    {
        var arguments = enabled
            ? new[]
            {
                "/Create",
                "/TN", ScheduledTaskName,
                "/TR", $"\"{Application.ExecutablePath}\"",
                "/SC", "ONLOGON",
                "/RL", "HIGHEST",
                "/F"
            }
            : new[] { "/Delete", "/TN", ScheduledTaskName, "/F" };

        RunElevatedSchtasks(arguments);
        RemoveLegacyRegistryEntry();
    }

    private static void RunElevatedSchtasks(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetSchtasksPath(),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("O Agendador de Tarefas não foi iniciado.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"O Agendador de Tarefas retornou o código {process.ExitCode}.");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("A autorização do Windows foi cancelada.", ex);
        }
    }

    private static string GetSchtasksPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        "System32",
        "schtasks.exe");

    private static void RemoveLegacyRegistryEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
