using System.Diagnostics;

namespace PdvBarcodeFilter;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly FilterSettings _settings;
    private readonly RealtimeDiagnosticHub _diagnostics;
    private readonly ScanCoordinator _coordinator;
    private readonly KeyboardHook _hook;
    private readonly RawInputMonitor? _rawInputMonitor;
    private readonly Control _dispatcher;
    private readonly StatusIconSet _icons;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _simulatorItem;
    private readonly System.Windows.Forms.Timer _healthTimer;
    private readonly GlobalHotkey? _globalHotkey;
    private readonly ScannerSimulatorServer? _simulatorServer;
    private long _lastHookRefreshTick;
    private long _nextHookErrorNotificationAt;
    private long _lastDiagnosticHeartbeatTick;
    private long _lastHeartbeatWrittenTick;
    private long _errorStateUntil;
    private bool _exiting;
    private volatile bool _simulatorEnabled;
    private DiagnosticForm? _diagnosticForm;
    private string? _lastInferredDiagnosis;
    private string? _lastHeartbeatState;

    public TrayApplicationContext(
        FilterSettings settings,
        RealtimeDiagnosticHub diagnostics)
    {
        _settings = settings;
        _diagnostics = diagnostics;
        _simulatorEnabled = settings.SimulatorInputEnabled;
        _dispatcher = new Control();
        _ = _dispatcher.Handle;
        _icons = new StatusIconSet();
        _coordinator = new ScanCoordinator(
            settings,
            new KeyboardSender(),
            PostNotification,
            diagnostics);

        try
        {
            _hook = new KeyboardHook(
                settings.MaxScanLength,
                _coordinator.TryPost,
                diagnostics,
                settings.KeyboardPassthrough)
            {
                IsPaused = settings.StartPaused
            };
        }
        catch
        {
            _coordinator.Dispose();
            _icons.Dispose();
            _dispatcher.Dispose();
            throw;
        }

        try
        {
            _rawInputMonitor = new RawInputMonitor(diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Write("RAW_MONITOR_ERROR", $"{ex.GetType().Name}: {ex.Message}");
        }

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _pauseItem = new ToolStripMenuItem();
        _pauseItem.Click += (_, _) => SetPaused(!_hook.IsPaused);

        _startupItem = new ToolStripMenuItem("Iniciar com o Windows (elevado)")
        {
            Checked = StartupRegistration.IsEnabled(),
            CheckOnClick = false
        };
        _startupItem.Click += (_, _) => ToggleStartup();

        var openLogs = new ToolStripMenuItem("Abrir pasta de logs");
        openLogs.Click += (_, _) => OpenFolder(_diagnostics.DiagnosticDirectory);

        var showDiagnostics = new ToolStripMenuItem("Diagnóstico em tempo real");
        showDiagnostics.Click += (_, _) => ShowRealtimeDiagnostics();

        _simulatorItem = new ToolStripMenuItem("Permitir simulador (somente teste)")
        {
            Checked = _simulatorEnabled,
            CheckOnClick = false
        };
        _simulatorItem.Click += (_, _) => ToggleSimulator();

        var exit = new ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(openLogs);
        menu.Items.Add(showDiagnostics);
        menu.Items.Add(_simulatorItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _trayIcon = new NotifyIcon
        {
            Icon = _icons.Active,
            Text = "Filtro de código de barras PDV",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => SetPaused(!_hook.IsPaused);

        try
        {
            _simulatorServer = new ScannerSimulatorServer(
                HandleSimulatorScan,
                reportError: ex => diagnostics.Write(
                    "SIMULATOR_PIPE_ERROR",
                    $"{ex.GetType().Name}: {ex.Message}"));
            diagnostics.Write(
                "SIMULATOR_PIPE",
                $"Canal local inicializado | enabled={_simulatorEnabled}");
        }
        catch (Exception ex)
        {
            diagnostics.Write("SIMULATOR_PIPE_ERROR", $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var gesture = HotkeyGesture.Parse(settings.ToggleHotkey);
            _globalHotkey = new GlobalHotkey(
                gesture,
                () => SetPaused(!_hook.IsPaused, fromHotkey: true),
                diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Write("HOTKEY_ERROR", $"{ex.GetType().Name}: {ex.Message}");
            _trayIcon.ShowBalloonTip(
                3000,
                "Filtro PDV",
                "O atalho configurado não pôde ser registrado.",
                ToolTipIcon.Warning);
        }

        _healthTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _healthTimer.Tick += (_, _) => HealthTimerTick();
        _healthTimer.Start();

        UpdateMenuState();
        _diagnostics.Write(
            "APP_READY",
            $"Filtro, hook, bandeja e monitor Raw Input inicializados | keyboard_passthrough={settings.KeyboardPassthrough} " +
            $"toggle_hotkey={settings.ToggleHotkey} beep_on_error={settings.BeepOnError} " +
            $"detailed_input_logging={settings.DetailedInputLogging} simulator_enabled={_simulatorEnabled} " +
            $"auxiliary_from_second={settings.UseAuxiliaryFromSecondScan}.");
        _diagnostics.Write(
            "DIAG_GUIDE",
            "Fluxo útil esperado: SCAN_COMPLETE -> PARSE_OK -> SEND_BEGIN -> SEND_OK. Eventos por tecla exigem DetailedInputLogging=true.");
        _ = PnpDiagnosticsCollector.CollectAsync(_diagnostics);
    }

    private void HealthTimerTick()
    {
        _hook.CheckForTimedOutScan(_settings.InterCharacterTimeoutMs);

        var now = Environment.TickCount64;
        if (now - _lastDiagnosticHeartbeatTick >= _settings.DiagnosticHeartbeatMs)
        {
            _lastDiagnosticHeartbeatTick = now;
            var hook = _hook.GetStatistics();
            var queue = _coordinator.GetStatistics();
            var raw = _rawInputMonitor?.GetStatistics() ?? new RawInputStatistics(0, 0);
            var heartbeatState =
                $"{hook.Paused}|{hook.CompletedScans}|{queue.QueuedScans}|{queue.ValidScans}|" +
                $"{queue.InvalidScans}|{queue.SentScans}|{queue.SendFailures}";
            if (!string.Equals(heartbeatState, _lastHeartbeatState, StringComparison.Ordinal) ||
                now - _lastHeartbeatWrittenTick >= 300000)
            {
                _lastHeartbeatState = heartbeatState;
                _lastHeartbeatWrittenTick = now;
                _diagnostics.Write(
                    "HEARTBEAT",
                    $"paused={hook.Paused} keyboard_passthrough={hook.KeyboardPassthrough} " +
                    $"physical_keydowns={hook.PhysicalKeyDowns} injected_keydowns={hook.InjectedKeyDowns} " +
                    $"completed_scans={hook.CompletedScans} pending_chars={hook.PendingCharacters} " +
                    $"raw_keyboard={raw.KeyboardEvents} raw_hid={raw.HidReports} queued={queue.QueuedScans} " +
                    $"valid={queue.ValidScans} invalid={queue.InvalidScans} sent={queue.SentScans} " +
                    $"send_failures={queue.SendFailures}");
            }

            WriteInferredDiagnosis(hook, raw, queue);
        }

        if (now - _lastHookRefreshTick >= _settings.WatchdogIntervalMs)
        {
            _lastHookRefreshTick = now;
            try
            {
                _hook.RefreshHookIfIdle();
            }
            catch (Exception ex)
            {
                // Nova tentativa em 5 segundos; o alerta continua limitado a
                // uma ocorrência a cada 5 minutos.
                _lastHookRefreshTick = now - _settings.WatchdogIntervalMs + 5000;
                _diagnostics.Write("HOOK_REFRESH_ERROR", $"{ex.GetType().Name}: {ex.Message}");
                if (now >= _nextHookErrorNotificationAt)
                {
                    _nextHookErrorNotificationAt = now + 300000;
                    HandleNotification(new FilterNotification(
                        FilterNotificationKind.Error,
                        "Falha ao recadastrar a captura de teclado."));
                }
            }
        }

        if (_errorStateUntil > 0 && now >= _errorStateUntil)
        {
            _errorStateUntil = 0;
            UpdateMenuState();
        }
    }

    private void WriteInferredDiagnosis(
        KeyboardHookStatistics hook,
        RawInputStatistics raw,
        ScanCoordinatorStatistics queue)
    {
        string? diagnosis = null;
        if (raw.HidReports > 0 && hook.PhysicalKeyDowns == 0)
        {
            diagnosis = "SINAL_HID_POS_SEM_TECLADO | O leitor enviou relatórios HID POS, mas o hook de teclado não recebeu teclas.";
        }
        else if (queue.SendFailures > 0)
        {
            diagnosis = "FALHA_NO_ENVIO | O QR foi interpretado, mas SendInput falhou ao entregar o resultado.";
        }
        else if (queue.InvalidScans > 0 && queue.ValidScans == 0)
        {
            diagnosis = "LEITURA_REJEITADA | O hook concluiu a leitura, mas a estrutura/delimitação não foi aceita.";
        }
        else if (queue.SentScans > 0)
        {
            diagnosis = "FLUXO_INTERNO_COMPLETO | O filtro capturou, interpretou e chamou SendInput; confirme foco e privilégios do PDV se a tela não reagiu.";
        }

        if (diagnosis is null || string.Equals(diagnosis, _lastInferredDiagnosis, StringComparison.Ordinal))
        {
            return;
        }

        _lastInferredDiagnosis = diagnosis;
        _diagnostics.Write("DIAGNOSIS", diagnosis);
    }

    private void PostNotification(FilterNotification notification)
    {
        if (_exiting || _dispatcher.IsDisposed)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(new Action(() => HandleNotification(notification)));
        }
        catch (InvalidOperationException) when (_exiting)
        {
        }
    }

    private void HandleNotification(FilterNotification notification)
    {
        if (_exiting)
        {
            return;
        }

        var isError = notification.Kind is
            FilterNotificationKind.InvalidScan or
            FilterNotificationKind.ScannerBlocked or
            FilterNotificationKind.Error;

        if (isError)
        {
            _errorStateUntil = Environment.TickCount64 + 3000;
        }

        UpdateMenuState();

        if (!_settings.ShowConfirmations)
        {
            return;
        }

        var icon = isError ? ToolTipIcon.Warning : ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(1000, "Filtro PDV", notification.Message, icon);
    }

    private void SetPaused(bool paused, bool fromHotkey = false)
    {
        _hook.IsPaused = paused;
        _errorStateUntil = 0;
        UpdateMenuState();
        _diagnostics.Write(
            paused ? "FILTER_PAUSED" : "FILTER_RESUMED",
            fromHotkey ? "Alterado pelo atalho global." : "Alterado pelo menu/bandeja.");
        if (fromHotkey)
        {
            _trayIcon.ShowBalloonTip(
                1500,
                "Filtro PDV",
                paused ? "Filtro pausado; teclado e scanner liberados." : "Filtro reativado.",
                ToolTipIcon.Info);
        }
    }

    private void ToggleSimulator()
    {
        _simulatorEnabled = !_simulatorEnabled;
        _simulatorItem.Checked = _simulatorEnabled;
        _diagnostics.Write(
            _simulatorEnabled ? "SIMULATOR_ENABLED" : "SIMULATOR_DISABLED",
            "Alterado pelo menu da bandeja; válido somente para esta execução.");
        _trayIcon.ShowBalloonTip(
            1800,
            "Filtro PDV",
            _simulatorEnabled
                ? "Simulador permitido nesta execução."
                : "Simulador desativado.",
            ToolTipIcon.Info);
    }

    private SimulatorSubmissionResult HandleSimulatorScan(string rawCode)
    {
        if (_exiting)
        {
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Unavailable,
                "O filtro está sendo encerrado.");
        }

        if (!_simulatorEnabled)
        {
            _diagnostics.Write("SIMULATOR_REJECT", "Modo de teste desativado.");
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Disabled,
                "No ícone do filtro, habilite “Permitir simulador (somente teste)”.");
        }

        if (_hook.IsPaused)
        {
            _diagnostics.Write("SIMULATOR_REJECT", "Filtro pausado.");
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Paused,
                "O filtro está pausado. Reative-o antes do teste.");
        }

        if (rawCode.Length > _settings.MaxScanLength)
        {
            _diagnostics.Write(
                "SIMULATOR_REJECT",
                $"Leitura excede MaxScanLength | length={rawCode.Length}");
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Invalid,
                "A leitura simulada excede o tamanho máximo configurado.");
        }

        var accepted = _coordinator.TryPost(new CapturedScan(
            rawCode,
            Overflowed: false,
            TimedOut: false,
            ReplaceCurrentField: true));
        if (!accepted)
        {
            _diagnostics.Write(
                "SIMULATOR_REJECT",
                $"Estrutura inválida | raw={_diagnostics.DisplayValue(rawCode)}");
            return new SimulatorSubmissionResult(
                SimulatorSubmissionStatus.Invalid,
                "O filtro não reconheceu UID, código padrão e código auxiliar.");
        }

        _diagnostics.Write(
            "SIMULATOR_SCAN",
            $"Leitura de teste aceita | raw={_diagnostics.DisplayValue(rawCode)}");
        return SimulatorSubmissionResult.Accepted();
    }

    private void UpdateMenuState()
    {
        if (_hook.IsPaused)
        {
            _statusItem.Text = $"Estado: PAUSADO ({_settings.ToggleHotkey} para reativar)";
            _pauseItem.Text = $"Reativar filtro ({_settings.ToggleHotkey})";
            _trayIcon.Text = "Filtro PDV — PAUSADO";
            _trayIcon.Icon = _icons.Paused;
        }
        else if (_errorStateUntil > Environment.TickCount64)
        {
            _statusItem.Text = "Estado: ATENÇÃO";
            _pauseItem.Text = $"Pausar filtro ({_settings.ToggleHotkey})";
            _trayIcon.Text = "Filtro PDV — ATENÇÃO";
            _trayIcon.Icon = _icons.Error;
        }
        else
        {
            _statusItem.Text = "Estado: ATIVO";
            _pauseItem.Text = $"Pausar filtro ({_settings.ToggleHotkey})";
            _trayIcon.Text = "Filtro PDV — ATIVO";
            _trayIcon.Icon = _icons.Active;
        }
    }

    private void ToggleStartup()
    {
        try
        {
            var enable = !StartupRegistration.IsEnabled();
            StartupRegistration.SetEnabled(enable);
            _startupItem.Checked = enable;
            _diagnostics.Write(
                enable ? "STARTUP_ENABLED" : "STARTUP_DISABLED",
                "Configuração de inicialização elevada alterada.");
        }
        catch (Exception ex)
        {
            _diagnostics.Write("STARTUP_ERROR", $"{ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(ex.Message, "Filtro PDV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _diagnostics.Write("OPEN_FOLDER_ERROR", $"{ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(ex.Message, "Filtro PDV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowRealtimeDiagnostics()
    {
        if (_diagnosticForm is null || _diagnosticForm.IsDisposed)
        {
            _diagnosticForm = new DiagnosticForm(_diagnostics);
        }

        if (!_diagnosticForm.Visible)
        {
            _diagnosticForm.Show();
        }

        _diagnostics.Write(
            "DIAGNOSTIC_UI",
            $"Janela exibida sem ativação | arquivo={_diagnostics.DiagnosticFilePath}");
    }

    public void RequestExit() => ExitThread();

    protected override void ExitThreadCore()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _diagnostics.Write("APP_EXIT", "Encerramento solicitado pelo operador ou teste de ciclo de vida.");
        _healthTimer.Stop();
        _healthTimer.Dispose();
        _simulatorServer?.Dispose();
        _globalHotkey?.Dispose();
        _hook.Dispose();
        _rawInputMonitor?.Dispose();
        _coordinator.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _icons.Dispose();
        _diagnosticForm?.Dispose();
        _dispatcher.Dispose();
        base.ExitThreadCore();
    }
}
