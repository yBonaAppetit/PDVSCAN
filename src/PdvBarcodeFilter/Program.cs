using System.Threading;
using System.Diagnostics;

namespace PdvBarcodeFilter;

internal static class Program
{
    private const string MutexName = @"Local\PdvBarcodeFilter-8C642EF4-982D-43CF-A44E-70A64FD1256B";

    [STAThread]
    private static void Main(string[] args)
    {
        var lifecycleTest = args.Contains("--lifecycle-test", StringComparer.OrdinalIgnoreCase);

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = BuiltInSelfTest.Run() ? 0 : 1;
            return;
        }

        if (args.Contains("--install-startup", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--remove-startup", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var enable = args.Contains("--install-startup", StringComparer.OrdinalIgnoreCase);
                if (enable || StartupRegistration.IsEnabled())
                {
                    StartupRegistration.SetEnabled(enable);
                }

                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 2;
            }

            return;
        }

        using var singleInstance = new Mutex(true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            var answer = MessageBox.Show(
                "Ainda existe uma instância do Filtro PDV em execução.\n\n" +
                "Deseja encerrá-la e iniciar esta versão?",
                "Filtro PDV",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes ||
                !TerminateExistingInstances() ||
                !WaitForMutex(singleInstance))
            {
                MessageBox.Show(
                    "A instância anterior não pôde ser encerrada. " +
                    "Finalize PdvBarcodeFilter no Gerenciador de Tarefas e tente novamente.",
                    "Filtro PDV",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        ApplicationConfiguration.Initialize();

        var settingsResult = lifecycleTest
            ? new SettingsLoadResult(new FilterSettings
            {
                StartPaused = true,
                ShowConfirmations = false
            }, null)
            : SettingsLoader.Load();
        using var diagnostics = new RealtimeDiagnosticHub(settingsResult.Settings);

        diagnostics.Write(
            "APP_START",
            $"version={Application.ProductVersion} pid={Environment.ProcessId} os={Environment.OSVersion} " +
            $"x64_process={Environment.Is64BitProcess} path={Application.ExecutablePath} " +
            $"input_struct_size={KeyboardSender.NativeInputStructureSize} " +
            $"raw_logging={diagnostics.LogRawData} detailed_input_logging={diagnostics.DetailedInputLogging} " +
            $"beep_on_error={settingsResult.Settings.BeepOnError} file={diagnostics.DiagnosticFilePath}");

        if (settingsResult.Error is not null)
        {
            diagnostics.Write(
                "SETTINGS_ERROR",
                $"Configuração inválida; valores padrão utilizados | {settingsResult.Error.GetType().Name}: {settingsResult.Error.Message}");
            MessageBox.Show(
                $"Não foi possível ler filtersettings.json. Os valores padrão serão usados.\n\n{settingsResult.Error.Message}",
                "Filtro PDV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Application.ThreadException += (_, args) =>
            diagnostics.Write("UI_UNHANDLED_ERROR", $"{args.Exception.GetType().Name}: {args.Exception.Message}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            diagnostics.Write("FATAL_ERROR", args.ExceptionObject?.ToString() ?? "<sem-detalhes>");

        try
        {
            using var context = new TrayApplicationContext(settingsResult.Settings, diagnostics);
            // Dez segundos permitem validar também o inventário PnP assíncrono
            // em terminais mais lentos sem afetar a execução normal.
            using var lifecycleTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            if (lifecycleTest)
            {
                lifecycleTimer.Tick += (_, _) =>
                {
                    lifecycleTimer.Stop();
                    context.RequestExit();
                };
                lifecycleTimer.Start();
            }

            Application.Run(context);
            // Garante que nenhuma thread auxiliar mantenha um processo invisível
            // depois que o operador escolheu Sair.
            diagnostics.Dispose();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            diagnostics.Write("APP_START_ERROR", $"{ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(
                $"Não foi possível iniciar o filtro.\n\n{ex.Message}",
                "Filtro PDV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool WaitForMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static bool TerminateExistingInstances()
    {
        var currentProcessId = Environment.ProcessId;
        var success = true;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == currentProcessId)
                {
                    continue;
                }

                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is null)
                    {
                        continue;
                    }

                    var version = FileVersionInfo.GetVersionInfo(path);
                    if (!string.Equals(version.ProductName, "PdvBarcodeFilter", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                    success &= process.WaitForExit(5000);
                }
                catch
                {
                    success = false;
                }
            }
        }

        return success;
    }
}
