namespace PdvBarcodeFilter;

public sealed class DiagnosticForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private readonly RealtimeDiagnosticHub _diagnostics;
    private readonly RichTextBox _output;

    public DiagnosticForm(RealtimeDiagnosticHub diagnostics)
    {
        _diagnostics = diagnostics;
        Text = "Diagnóstico em tempo real — Filtro PDV";
        Width = 900;
        Height = 480;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(
            Math.Max(area.Left, area.Right - Width - 20),
            Math.Max(area.Top, area.Bottom - Height - 20));

        _output = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.FromArgb(225, 225, 225),
            Font = new Font(FontFamily.GenericMonospace, 9f),
            WordWrap = false,
            DetectUrls = false
        };
        Controls.Add(_output);

        foreach (var line in diagnostics.Snapshot())
        {
            AppendLine(line);
        }

        diagnostics.LineAdded += OnLineAdded;
        FormClosed += (_, _) => diagnostics.LineAdded -= OnLineAdded;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate;
            return parameters;
        }
    }

    private void OnLineAdded(string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() => AppendLine(line)));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void AppendLine(string line)
    {
        if (_output.TextLength > 1_500_000)
        {
            _output.Clear();
            _output.AppendText("--- visualização reiniciada; o arquivo em disco continua completo ---\r\n");
        }

        _output.AppendText(line + Environment.NewLine);
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
    }
}
