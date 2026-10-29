namespace AetherGui;

// Light theme: soft gray window, white panels with thin borders, Windows blue for the main action.
static class Palette
{
    public static readonly Color Background = ColorTranslator.FromHtml("#F3F3F3");
    public static readonly Color Surface = Color.White;
    public static readonly Color LogBackground = ColorTranslator.FromHtml("#FAFAFA");
    public static readonly Color Border = ColorTranslator.FromHtml("#DDDDDD");
    public static readonly Color ButtonBorder = ColorTranslator.FromHtml("#C8C8C8");
    public static readonly Color Hover = ColorTranslator.FromHtml("#F0F0F0");
    public static readonly Color Pressed = ColorTranslator.FromHtml("#E5E5E5");
    public static readonly Color MutedText = ColorTranslator.FromHtml("#605E5C");
    public static readonly Color Accent = ColorTranslator.FromHtml("#0078D7");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#106EBE");
    public static readonly Color AccentPressed = ColorTranslator.FromHtml("#005A9E");
    public static readonly Color Connected = ColorTranslator.FromHtml("#107C10");
    public static readonly Color Connecting = ColorTranslator.FromHtml("#CA5010");
    public static readonly Color Disconnected = ColorTranslator.FromHtml("#C42B1C");
}

partial class Form1
{
    private Button btnConnect = null!;
    private Button btnInfo = null!;
    private Button btnCoreSettings = null!;
    private Button btnCoreInput = null!;
    private Button btnUpdates = null!;
    private Label lblStatus = null!;
    private Label lblDelay = null!;
    private Label lblProtocol = null!;
    private ComboBox cboProtocol = null!;
    private Label lblScan = null!;
    private ComboBox cboScan = null!;
    private RichTextBox txtLog = null!;
    private CheckBox chkStartup = null!;
    private CheckBox chkSysProxy = null!;
    private CheckBox chkPsiphon = null!;
    private ProgressBar prgConnecting = null!;

    // Flat button with a thin border; the primary one is filled blue with white text.
    private static Button FlatButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand,
            AutoSize = true, MinimumSize = new Size(110, 30), Margin = new Padding(0, 0, 8, 8),
            BackColor = primary ? Palette.Accent : Palette.Surface, ForeColor = primary ? Color.White : SystemColors.ControlText
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Palette.Accent : Palette.ButtonBorder;
        button.FlatAppearance.MouseOverBackColor = primary ? Palette.AccentHover : Palette.Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? Palette.AccentPressed : Palette.Pressed;
        return button;
    }

    // White panel with a thin light gray border, named so tests can find it.
    private static Panel Card(string name)
    {
        var card = new Panel { Name = name, Dock = DockStyle.Fill, BackColor = Palette.Surface, Padding = new Padding(16) };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Resize += (_, _) => card.Invalidate();
        return card;
    }

    private void InitializeComponent()
    {
        Font = new Font("Segoe UI", 9.75F);
        var bold = new Font("Segoe UI Semibold", 9.75F);
        var header = new Font("Segoe UI Semibold", 11F);

        btnConnect = FlatButton("Connect securely", primary: true);
        btnConnect.AutoSize = false;
        btnConnect.Dock = DockStyle.Top;
        btnConnect.Height = 42;
        btnConnect.Font = bold;
        btnConnect.Margin = new Padding(0, 8, 0, 12);
        btnConnect.Click += btnConnect_Click;

        btnInfo = FlatButton("Proxy info");
        btnInfo.AccessibleDescription = "Write proxy ports and security recommendations to the activity log";
        btnInfo.Click += (_, _) => ShowProxyInfo();
        btnCoreSettings = FlatButton("Core settings");
        btnCoreSettings.Click += (_, _) => ShowCoreSettings();
        btnCoreInput = FlatButton("Core input");
        btnCoreInput.Enabled = false;
        btnCoreInput.Click += async (_, _) => await SendCoreInputAsync();
        btnUpdates = FlatButton("Updates");
        btnUpdates.AccessibleDescription = "Check for a new AetherGui release and a newer core";
        btnUpdates.Click += async (_, _) => await CheckUpdatesAsync(interactive: true);

        lblStatus = new Label { Text = "●  Disconnected", ForeColor = Palette.Disconnected, AutoSize = true, Font = new Font("Segoe UI Semibold", 13F), Margin = new Padding(0, 0, 0, 0), AccessibleName = "Connection status: Disconnected" };
        lblDelay = new Label { Text = "Delay: -", ForeColor = Palette.MutedText, AutoSize = true, Margin = new Padding(12, 6, 0, 0) };
        prgConnecting = new ProgressBar
        {
            Style = SystemInformation.UIEffectsEnabled ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
            Value = 100, MarqueeAnimationSpeed = 30, Dock = DockStyle.Top, Height = 4, Visible = false,
            Margin = new Padding(0, 4, 0, 0)
        };

        lblProtocol = new Label { Text = "Protocol", Font = bold, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        cboProtocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 360, Margin = new Padding(0, 0, 0, 4), AccessibleName = "Connection protocol" };
        lblScan = new Label { Text = "Server search", Font = bold, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        cboScan = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 360, Margin = new Padding(0, 0, 0, 4), AccessibleName = "Server search mode" };

        chkStartup = new CheckBox { Text = "Connect on startup", AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        chkSysProxy = new CheckBox { Text = "Set Windows system proxy", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        chkPsiphon = new CheckBox { Text = "Open Psiphon after connecting", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

        var status = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        status.Controls.AddRange([lblStatus, lblDelay]);
        var helper = new Label
        {
            Text = "Choose a route, then connect. Auto switches fast routes when a connection drops.",
            ForeColor = Palette.MutedText, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true, Margin = Padding.Empty
        };

        var controlLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 11, Margin = Padding.Empty };
        controlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // fit the panel, never the content
        for (var i = 0; i < 11; i++) controlLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Connect sits right under the status so it stays in view; the rest scrolls if large text needs more room.
        controlLayout.Controls.Add(status, 0, 0);
        controlLayout.Controls.Add(prgConnecting, 0, 1);
        controlLayout.Controls.Add(btnConnect, 0, 2);
        controlLayout.Controls.Add(helper, 0, 3);
        controlLayout.Controls.Add(lblProtocol, 0, 4);
        controlLayout.Controls.Add(cboProtocol, 0, 5);
        controlLayout.Controls.Add(lblScan, 0, 6);
        controlLayout.Controls.Add(cboScan, 0, 7);
        controlLayout.Controls.Add(chkStartup, 0, 8);
        controlLayout.Controls.Add(chkSysProxy, 0, 9);
        controlLayout.Controls.Add(chkPsiphon, 0, 10);

        // Secondary actions as an even 2x2 grid that always fits the panel width.
        var actions = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 12, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var (button, i) in new[] { btnCoreSettings, btnCoreInput, btnInfo, btnUpdates }.Select((b, i) => (b, i)))
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.MinimumSize = new Size(0, 32);
            button.AutoEllipsis = true;
            button.Margin = new Padding(i % 2 == 0 ? 0 : 4, 0, i % 2 == 0 ? 4 : 0, 8);
            actions.Controls.Add(button, i % 2, i / 2);
        }

        var leftStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var controlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty }; // scrollbar only when needed
        controlScroll.Controls.Add(controlLayout);
        leftStack.Controls.Add(controlScroll, 0, 0);
        leftStack.Controls.Add(actions, 0, 1);

        // Docked controls lay out last-added first: the header takes the top, then the body fills the rest.
        var connectionCard = Card("connectionPanel");
        connectionCard.Margin = new Padding(0, 0, 6, 0);
        connectionCard.Controls.Add(leftStack);
        connectionCard.Controls.Add(new Label { Text = "Connection", Font = header, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 10) });

        txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Palette.LogBackground,
            Font = new Font("Consolas", 9.5F), DetectUrls = true, AccessibleName = "Connection activity log"
        };
        // Thin light gray border: a 1px gray frame around a padded light panel holding the borderless log.
        var logInner = new Panel { Dock = DockStyle.Fill, BackColor = Palette.LogBackground, Padding = new Padding(8) };
        logInner.Controls.Add(txtLog);
        var logFrame = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Border, Padding = new Padding(1) };
        logFrame.Controls.Add(logInner);

        var clear = new LinkLabel { Text = "Clear logs", AutoSize = true, Anchor = AnchorStyles.Right, LinkColor = Palette.Accent, ActiveLinkColor = Palette.AccentPressed, LinkBehavior = LinkBehavior.HoverUnderline, AccessibleDescription = "Clear the activity log" };
        clear.LinkClicked += (_, _) => txtLog.Clear();
        var logHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 10) };
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.Controls.Add(new Label { Text = "Activity", Font = header, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty }, 0, 0);
        logHeader.Controls.Add(clear, 1, 0);

        var activityCard = Card("activityPanel");
        activityCard.Margin = new Padding(6, 0, 0, 0);
        activityCard.Controls.Add(logFrame);
        activityCard.Controls.Add(logHeader);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 1, BackColor = Palette.Background };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); // a share, not pixels: follows display scaling
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        shell.Controls.Add(connectionCard, 0, 0);
        shell.Controls.Add(activityCard, 1, 0);

        Text = "Aether — Private network client";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Palette.Background;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Controls.Add(shell);
    }
}
