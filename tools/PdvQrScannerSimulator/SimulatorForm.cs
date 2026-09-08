using PdvBarcodeFilter;

namespace PdvQrScannerSimulator;

public sealed class SimulatorForm : Form
{
    private const string FirstSample =
        "00000000-0000-0000-0000-000000000001}X96UG}2W7LX1";
    private const string SecondSample =
        "00000000-0000-0000-0000-000000000002}8H57E}2W7LZ5";

    private readonly ComboBox _sampleSelector;
    private readonly TextBox _rawCode;
    private readonly NumericUpDown _delaySeconds;
    private readonly Button _sendButton;
    private readonly Label _status;

    public SimulatorForm()
    {
        Text = "Simulador de leitor QR — Filtro PDV";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(690, 360);
        ClientSize = new Size(690, 360);
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            Text = "Simular leitura de QR Code",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20)
        };
        var instructions = new Label
        {
            Text = "No filtro, habilite “Permitir simulador”. Ao enviar, esta janela será minimizada;\n" +
                   "selecione o campo do PDV durante a contagem regressiva.",
            AutoSize = true,
            Location = new Point(24, 52)
        };

        _sampleSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(24, 100),
            Width = 630
        };
        _sampleSelector.Items.AddRange(new object[]
        {
            "Cupom 1 — X96UG / 2W7LX1",
            "Cupom 2 — 8H57E / 2W7LZ5"
        });
        _sampleSelector.SelectedIndexChanged += (_, _) => LoadSelectedSample();

        var rawLabel = new Label
        {
            Text = "Conteúdo bruto enviado pelo leitor:",
            AutoSize = true,
            Location = new Point(24, 140)
        };
        _rawCode = new TextBox
        {
            Location = new Point(24, 166),
            Width = 630
        };

        var delayLabel = new Label
        {
            Text = "Atraso para selecionar o PDV:",
            AutoSize = true,
            Location = new Point(24, 214)
        };
        _delaySeconds = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 10,
            Value = 3,
            Location = new Point(250, 210),
            Width = 60
        };
        var secondsLabel = new Label
        {
            Text = "segundos",
            AutoSize = true,
            Location = new Point(318, 214)
        };

        _sendButton = new Button
        {
            Text = "Enviar leitura",
            Location = new Point(24, 252),
            Width = 170,
            Height = 38
        };
        _sendButton.Click += async (_, _) => await SendAsync();

        _status = new Label
        {
            Text = "Pronto.",
            AutoEllipsis = true,
            Location = new Point(24, 310),
            Width = 630,
            Height = 30
        };

        Controls.AddRange(new Control[]
        {
            title,
            instructions,
            _sampleSelector,
            rawLabel,
            _rawCode,
            delayLabel,
            _delaySeconds,
            secondsLabel,
            _sendButton,
            _status
        });

        _sampleSelector.SelectedIndex = 0;
        AcceptButton = _sendButton;
    }

    private void LoadSelectedSample()
    {
        _rawCode.Text = _sampleSelector.SelectedIndex == 1
            ? SecondSample
            : FirstSample;
    }

    private async Task SendAsync()
    {
        var rawCode = _rawCode.Text.Trim();
        if (!HasCouponStructure(rawCode))
        {
            _status.Text = "Leitura inválida: use UID|PADRÃO|AUXILIAR ou UID}PADRÃO}AUXILIAR.";
            return;
        }

        _sendButton.Enabled = false;
        _sampleSelector.Enabled = false;
        _rawCode.Enabled = false;
        _delaySeconds.Enabled = false;

        var seconds = (int)_delaySeconds.Value;
        WindowState = FormWindowState.Minimized;
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            _status.Text = $"Enviando em {remaining} segundo(s). Selecione o campo do PDV...";
            await Task.Delay(1000);
        }

        _status.Text = "Enviando leitura ao filtro...";
        var result = await ScannerSimulatorClient.SendAsync(rawCode);
        _status.Text = result.IsAccepted
            ? "Leitura aceita. Volte ao PDV para conferir o resultado."
            : result.Message;

        _sendButton.Enabled = true;
        _sampleSelector.Enabled = true;
        _rawCode.Enabled = true;
        _delaySeconds.Enabled = true;
    }

    private static bool HasCouponStructure(string rawCode)
    {
        var fields = rawCode.Split(new[] { '|', '}' }, StringSplitOptions.None);
        return fields.Length >= 3 &&
               !string.IsNullOrWhiteSpace(fields[0]) &&
               !string.IsNullOrWhiteSpace(fields[1]) &&
               !string.IsNullOrWhiteSpace(fields[2]);
    }
}
