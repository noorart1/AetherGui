namespace AetherGui;

// "Shield dark + connected green" palette (ui-ux-pro-max): one place for every color the window uses.
static class Palette
{
    public static readonly Color Background = ColorTranslator.FromHtml("#0F172A");
    public static readonly Color Card = ColorTranslator.FromHtml("#192134");
    public static readonly Color Muted = ColorTranslator.FromHtml("#10192E");
    public static readonly Color Primary = ColorTranslator.FromHtml("#1E3A5F");
    public static readonly Color Foreground = Color.White;
    public static readonly Color MutedText = ColorTranslator.FromHtml("#94A3B8");
    public static readonly Color Connected = ColorTranslator.FromHtml("#22C55E");
    public static readonly Color Connecting = ColorTranslator.FromHtml("#F59E0B");
    public static readonly Color Disconnected = ColorTranslator.FromHtml("#EF4444");
}

partial class Form1
{
    private Button btnConnect;
    private Button btnInfo;
    private Label lblStatus;
    private Label lblDelay;
    private Label lblProtocol;
    private ComboBox cboProtocol;
    private RichTextBox txtLog;
    private CheckBox chkStartup;
    private CheckBox chkSysProxy;
    private ProgressBar prgConnecting;

    private static Button FlatButton(string text, Color back) => new()
    {
        Text = text, BackColor = back, ForeColor = Palette.Foreground, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = ControlPaint.Light(back, 0.25f), MouseDownBackColor = ControlPaint.Dark(back, 0.1f) },
        Font = new Font("Segoe UI Semibold", 10F)
    };

    private void InitializeComponent()
    {
        var ui = new Font("Segoe UI", 10F);
        btnConnect = FlatButton("Connect", Palette.Primary);
        btnConnect.Location = new Point(16, 16); btnConnect.Size = new Size(150, 44);
        btnInfo = FlatButton("Proxy info", Palette.Card);
        btnInfo.Location = new Point(16, 68); btnInfo.Size = new Size(150, 32);
        btnInfo.Click += (_, _) => ShowProxyInfo();
        lblStatus = new Label { Text = "● Disconnected", ForeColor = Palette.Disconnected, AutoSize = true, Location = new Point(182, 16), Font = new Font("Segoe UI Semibold", 12F) };
        lblDelay = new Label { Text = "Delay: -", ForeColor = Palette.MutedText, AutoSize = true, Location = new Point(184, 46), Font = ui };
        // shown only while a protocol is being tried, so a long scan never looks frozen; static when Windows animations are off
        prgConnecting = new ProgressBar { Style = SystemInformation.UIEffectsEnabled ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous, Value = 100, MarqueeAnimationSpeed = 30,Location = new Point(184, 40), Size = new Size(286, 4), Visible = false };
        lblProtocol = new Label { Text = "Protocol", ForeColor = Palette.MutedText, AutoSize = true, Location = new Point(182, 74), Font = ui };
        cboProtocol = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Location = new Point(250, 70), Width = 220,
            BackColor = Palette.Card, ForeColor = Palette.Foreground, Font = ui, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22
        };
        // a DropDownList ignores BackColor under visual styles, so paint items with the palette
        cboProtocol.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var back = new SolidBrush(selected ? Palette.Primary : Palette.Card);
            e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, cboProtocol.Items[e.Index].ToString(), e.Font, e.Bounds, Palette.Foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            e.DrawFocusRectangle();
        };
        chkStartup = new CheckBox { Text = "Connect on startup", ForeColor = Palette.Foreground, AutoSize = true, Location = new Point(490, 16), Font = ui };
        chkSysProxy = new CheckBox { Text = "Set system proxy", ForeColor = Palette.Foreground, AutoSize = true, Location = new Point(490, 42), Font = ui };
        txtLog = new RichTextBox
        {
            Location = new Point(16, 112), Size = new Size(652, 300), ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Palette.Muted, ForeColor = Palette.MutedText, Font = new Font("Consolas", 9.5F),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        btnConnect.Click += btnConnect_Click;

        Text = "Aether GUI";
        BackColor = Palette.Background;
        ClientSize = new Size(684, 428);
        MinimumSize = new Size(700, 300);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.AddRange(new Control[] { btnConnect, btnInfo, lblStatus, prgConnecting, lblDelay, lblProtocol, cboProtocol, chkStartup, chkSysProxy, txtLog });
    }
}
