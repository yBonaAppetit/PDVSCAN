using System.Diagnostics;

namespace PdvBarcodeFilter;

public static class PnpDiagnosticsCollector
{
    public static async Task CollectAsync(RealtimeDiagnosticHub diagnostics)
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
        {
            diagnostics.Write("PNP_ERROR", $"PowerShell não encontrado em {powershell}");
            return;
        }

        const string script = "& { " +
            "$devices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.InstanceId -match 'VID_1EAB&PID_2522' -or $_.FriendlyName -match 'Barcode|Scanner' -or $_.Class -match 'POS' }; " +
            "foreach ($device in $devices) { " +
            "$problem = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction SilentlyContinue).Data; " +
            "$problemStatus = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_ProblemStatus' -ErrorAction SilentlyContinue).Data; " +
            "$driver = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_DriverInfPath' -ErrorAction SilentlyContinue).Data; " +
            "$version = (Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_DriverVersion' -ErrorAction SilentlyContinue).Data; " +
            "[pscustomobject]@{ Status=$device.Status; Class=$device.Class; FriendlyName=$device.FriendlyName; InstanceId=$device.InstanceId; ProblemCode=$problem; ProblemStatus=$problemStatus; DriverInfPath=$driver; DriverVersion=$version } | ConvertTo-Json -Compress " +
            "} }";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = powershell,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("PowerShell não foi iniciado.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output))
            {
                diagnostics.Write("PNP_SNAPSHOT", "Nenhum scanner/POS correspondente foi encontrado pelo Get-PnpDevice.");
            }
            else
            {
                foreach (var line in output.Split(
                             new[] { "\r\n", "\n" },
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    diagnostics.Write("PNP_DEVICE", line);
                }
            }

            if (!string.IsNullOrWhiteSpace(error) || process.ExitCode != 0)
            {
                diagnostics.Write(
                    "PNP_ERROR",
                    $"exit={process.ExitCode} stderr={RealtimeDiagnosticHub.Sanitize(error.Trim())}");
            }
        }
        catch (Exception ex)
        {
            diagnostics.Write("PNP_ERROR", $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
