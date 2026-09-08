using PdvBarcodeFilter;

namespace PdvQrScannerSimulator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = RunSelfTest() ? 0 : 1;
            return;
        }

        var sendIndex = Array.FindIndex(
            args,
            argument => string.Equals(argument, "--send", StringComparison.OrdinalIgnoreCase));
        if (sendIndex >= 0)
        {
            if (sendIndex + 1 >= args.Length)
            {
                Environment.ExitCode = 3;
                return;
            }

            var result = ScannerSimulatorClient.SendAsync(args[sendIndex + 1])
                .GetAwaiter().GetResult();
            Environment.ExitCode = result.Status switch
            {
                SimulatorSubmissionStatus.Accepted => 0,
                SimulatorSubmissionStatus.Unavailable => 2,
                _ => 3
            };
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SimulatorForm());
    }

    private static bool RunSelfTest()
    {
        const string sample =
            "00000000-0000-0000-0000-000000000001}X96UG}2W7LX1";
        var pipeName = $"PdvBarcodeFilter.Simulator.SelfTest.{Guid.NewGuid():N}";
        using var server = new ScannerSimulatorServer(
            rawCode => string.Equals(rawCode, sample, StringComparison.Ordinal)
                ? SimulatorSubmissionResult.Accepted()
                : new SimulatorSubmissionResult(
                    SimulatorSubmissionStatus.Invalid,
                    "Conteúdo inesperado."),
            pipeName);
        var response = ScannerSimulatorClient.SendAsync(sample, pipeName: pipeName)
            .GetAwaiter().GetResult();
        return response.IsAccepted;
    }
}
