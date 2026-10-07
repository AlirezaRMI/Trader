using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net.NetworkInformation;

namespace Trader.Launcher;

internal sealed class LauncherForm : Form
{
    private static readonly Color Ink = Color.FromArgb(239, 243, 255);
    private static readonly Color Muted = Color.FromArgb(161, 175, 201);
    private static readonly Color Mint = Color.FromArgb(100, 225, 199);
    private readonly LocalServices _services;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PrivateFontCollection _fonts = new();
    private readonly List<Font> _ownedFonts = [];
    private readonly List<Button> _actions = [];
    private readonly Label _apiState;
    private readonly Label _bridgeState;
    private readonly Label _message;
    private readonly Label _activity;
    private readonly System.Windows.Forms.Timer _timer = new() {Interval = 1000};
    private readonly bool _autoStart;
    private bool _busy;
    private bool _refreshing;
    private bool _closing;
    private bool _allowClose;
    private int _frame;
    private Point? _drag;

    public LauncherForm(string root, bool autoStart)
    {
        _autoStart = autoStart;
        _services = new LocalServices(root);
        _services.Progress += ShowProgress;
        foreach (var name in new[] {"MODAM-REGULAR.TTF", "MODAM-SEMIBOLD.TTF"})
        {
            var path = Path.Combine(root, "Api", "wwwroot", "fonts", name);
            if (!File.Exists(path)) continue;
            try
            {
                _fonts.AddFontFile(path);
            }
            catch (ArgumentException)
            {
                /* Use the system font if an optional font is invalid. */
            }
        }

        Text = "Trader • مرکز کنترل";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 512);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = Color.FromArgb(13, 21, 39);
        ForeColor = Ink;
        Font = MakeFont(10);
        DoubleBuffered = true;
        RightToLeft = RightToLeft.Yes;
        AccessibleName = "مرکز کنترل برنامه و پل متاتریدر";

        AddLabel(this, "TRADER / LOCAL", new(28, 28, 160, 28), Mint, 9, false, ContentAlignment.MiddleLeft)
            .RightToLeft = RightToLeft.No;
        AddLabel(this, "مرکز کنترل معامله", new(192, 24, 272, 36), Ink, 16, true);
        AddLabel(this, "برنامه و پل متاتریدر، در یک پنجره", new(28, 68, 504, 24), Muted, 10);
        var close = AddButton("×", new(514, 16, 28, 28), Color.FromArgb(36, 47, 67), () => Close(), 14);
        close.AccessibleName = "بستن پنجره";
        var minimize = AddButton("−", new(482, 16, 28, 28), Color.FromArgb(36, 47, 67),
            () => WindowState = FormWindowState.Minimized, 12);
        minimize.AccessibleName = "کوچک‌کردن پنجره";

        _apiState = AddServiceCard("وب‌اپ", "HTTP · 5080", new(284, 110, 248, 108));
        _bridgeState = AddServiceCard("پل متاتریدر", "TCP · 8766", new(28, 110, 248, 108));
        AddAction("اجرای برنامه", new(284, 230, 248, 42), Color.FromArgb(39, 58, 81),
            () => RunAsync(() => _services.StartApiAsync(_lifetime.Token)));
        AddAction("اجرای پل", new(28, 230, 248, 42), Color.FromArgb(39, 58, 81),
            () => RunAsync(() => _services.StartBridgeAsync(_lifetime.Token)));
        AddAction("راه‌اندازی همه و بازکردن داشبورد", new(28, 282, 504, 48), Color.FromArgb(83, 71, 173),
            () => RunAsync(StartAllAsync), 11);

        var open = AddButton("داشبورد ↗", new(366, 342, 166, 36), Color.FromArgb(29, 43, 65),
            () => OpenTarget(LocalServices.DashboardLaunchUrl));
        open.AccessibleDescription = "باز کردن داشبورد در مرورگر پیش‌فرض";
        AddAction("توقف سرویس‌ها", new(197, 342, 159, 36), Color.FromArgb(56, 40, 57),
            () => RunAsync(StopWithConfirmationAsync), 9);
        AddButton("گزارش‌ها", new(28, 342, 159, 36), Color.FromArgb(29, 43, 65), () =>
        {
            Directory.CreateDirectory(_services.LogDirectory);
            OpenTarget(_services.LogDirectory);
        }, 9);

        var status = new GlassPanel {Bounds = new(28, 392, 504, 58)};
        Controls.Add(status);
        _activity = AddLabel(status, "●", new(470, 17, 18, 23), Muted, 10);
        _message = AddLabel(status, "آمادهٔ راه‌اندازی", new(12, 6, 450, 46), Muted, 9);
        AddLabel(this, "اجرای سرویس ≠ فعال‌سازی ترید؛ وضعیت EA را در داشبورد ببین.",
            new(28, 462, 504, 27), Muted, 8);

        // Drag only the empty title area, so buttons keep their ordinary click behavior.
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.Y < 105) _drag = e.Location;
        };
        MouseMove += (_, e) =>
        {
            if (_drag is { } origin) Location = new(Location.X + e.X - origin.X, Location.Y + e.Y - origin.Y);
        };
        MouseUp += (_, _) => _drag = null;
        _timer.Tick += async (_, _) =>
        {
            _activity.Text = _busy ? new[] {"◌", "◔", "◑", "◕"}[_frame++ % 4] : "●";
            _activity.ForeColor = _busy ? Mint : Muted;
            await RefreshStateAsync();
        };
        Shown += async (_, _) =>
        {
            UpdateRoundedRegion();
            _timer.Start();
            await RefreshStateAsync();
            if (_autoStart && !_closing) await RunAsync(StartAllAsync);
        };
        SizeChanged += (_, _) => UpdateRoundedRegion();
        FormClosing += OnClosing;
    }

    private Label AddServiceCard(string title, string endpoint, Rectangle bounds)
    {
        var card = new GlassPanel {Bounds = bounds};
        Controls.Add(card);
        AddLabel(card, title, new(14, 11, 218, 25), Ink, 11, true);
        AddLabel(card, endpoint, new(14, 38, 218, 23), Muted, 9, false, ContentAlignment.MiddleLeft)
            .RightToLeft = RightToLeft.No;
        return AddLabel(card, "در حال بررسی…", new(14, 73, 218, 23), Muted, 8);
    }

    private Label AddLabel(Control parent, string text, Rectangle bounds, Color color, float size,
        bool bold = false, ContentAlignment alignment = ContentAlignment.MiddleRight)
    {
        var label = new Label
        {
            Text = text, Bounds = bounds, ForeColor = color, BackColor = Color.Transparent,
            Font = MakeFont(size, bold), TextAlign = alignment, UseCompatibleTextRendering = true,
            RightToLeft = RightToLeft.Yes
        };
        parent.Controls.Add(label);
        return label;
    }

    private Button AddButton(string text, Rectangle bounds, Color color, Action action, float size = 10)
    {
        var button = new GlassButton(color)
        {
            Text = text, Bounds = bounds, ForeColor = Ink, Font = MakeFont(size),
            Cursor = Cursors.Hand, AccessibleName = text, RightToLeft = RightToLeft.Yes
        };
        button.Click += (_, _) => action();
        Controls.Add(button);
        return button;
    }

    private void AddAction(string text, Rectangle bounds, Color color, Func<Task> action, float size = 10)
    {
        _actions.Add(AddButton(text, bounds, color, async () => await action(), size));
    }

    private Font MakeFont(float size, bool bold = false)
    {
        var family = _fonts.Families.FirstOrDefault() ?? FontFamily.GenericSansSerif;
        var style = bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        var font = new Font(family, size, style, GraphicsUnit.Point);
        _ownedFonts.Add(font);
        return font;
    }

    private async Task StartAllAsync()
    {
        await _services.StartBridgeAsync(_lifetime.Token); // Starts/waits for API before Bridge.
        OpenTarget(LocalServices.DashboardLaunchUrl);
    }

    private async Task StopWithConfirmationAsync()
    {
        if (!_services.HasOwnedServices)
        {
            ShowProgress("سرویسی تحت کنترل این پنجره نیست؛ فرایندهای دیگر را متوقف نمی‌کنم.");
            return;
        }

        if (MessageBox.Show(this, "پایش و اتصال برنامه قطع می‌شود؛ معاملات باز بسته نمی‌شوند. ادامه می‌دهی؟",
                "توقف سرویس‌های همین پنجره", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading) == DialogResult.Yes)
            await _services.StopOwnedAsync();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closing) return;
        _busy = true;
        foreach (var button in _actions) button.Enabled = false;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException
                                          or System.ComponentModel.Win32Exception or NetworkInformationException)
        {
            ShowProgress(error is InvalidOperationException
                ? error.Message
                : "اجرای عملیات ممکن نشد؛ دسترسی و گزارش‌ها را بررسی کن.");
        }
        finally
        {
            _busy = false;
            if (!_closing)
            {
                foreach (var button in _actions) button.Enabled = true;
                await RefreshStateAsync();
            }
        }
    }

    private async Task RefreshStateAsync()
    {
        if (_refreshing || _closing) return;
        _refreshing = true;
        try
        {
            var state = await _services.ReadStateAsync(_lifetime.Token);
            if (_closing) return;
            _apiState.Text = state.ApiReady
                ? state.OwnsApi ? "● آماده · همین پنجره" : "● آماده · اجرای قبلی"
                : state.ApiOutdated ? "○ نسخهٔ قدیمی؛ نیاز به اجرای مجدد" : "○ پاسخ نمی‌دهد";
            _apiState.ForeColor = state.ApiReady ? Mint : Muted;
            _bridgeState.Text = state.BridgePortOpen
                ? state.OwnsBridge ? "● پورت آماده · همین پنجره" : "● پورت باز · اجرای دیگر"
                : "○ پورت بسته";
            _bridgeState.ForeColor = state.BridgePortOpen ? Mint : Muted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (NetworkInformationException)
        {
            ShowProgress("خواندن وضعیت پورت‌ها ممکن نشد.");
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ShowProgress(string message)
    {
        if (IsDisposed || _closing) return;
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() => ShowProgress(message));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        _message.Text = message;
    }

    private void OpenTarget(string target)
    {
        try
        {
            LocalServices.Open(target);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowProgress(target == LocalServices.DashboardLaunchUrl
                ? "مرورگر باز نشد؛ آدرس 127.0.0.1:5080 را دستی باز کن."
                : "بازکردن پوشهٔ گزارش‌ها ممکن نشد.");
        }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        if ((_services.HasOwnedServices || _busy) && MessageBox.Show(this,
                "با بستن پنجره، سرویس‌های همین لانچر متوقف می‌شوند؛ معاملات بروکر بسته نمی‌شوند. پنجره بسته شود؟",
                "بستن مرکز کنترل", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading) != DialogResult.Yes) return;
        _closing = true;
        _timer.Stop();
        _lifetime.Cancel();
        try
        {
            // Let a cancelled build/start finish its own cleanup before stopping child services.
            while (_busy) await Task.Delay(50);
            await _services.StopOwnedAsync();
            _allowClose = true;
            Close();
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _closing = false;
            MessageBox.Show(this, "توقف سرویس ممکن نشد؛ گزارش‌ها را بررسی کن. پنجره باز می‌ماند.", "Trader",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Minimize/restore and transparent children may ask the parent to paint a zero-sized client area.
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(16, 26, 46),
            Color.FromArgb(20, 22, 43), 125);
        e.Graphics.FillRectangle(background, ClientRectangle);
        using var glow = new SolidBrush(Color.FromArgb(15, 106, 92, 244));
        e.Graphics.FillEllipse(glow, -110, -160, 450, 370);
        using var teal = new SolidBrush(Color.FromArgb(10, 73, 230, 190));
        e.Graphics.FillEllipse(teal, Width - 280, 280, 380, 350);
        using var border = new Pen(Color.FromArgb(55, 162, 189, 232));
        using var shape = GlassPanel.Rounded(new(1, 1, ClientSize.Width - 3, ClientSize.Height - 3), 22);
        e.Graphics.DrawPath(border, shape);
        base.OnPaint(e);
    }

    private void UpdateRoundedRegion()
    {
        if (WindowState == FormWindowState.Minimized || ClientSize.Width < 4 || ClientSize.Height < 4) return;
        using var path = GlassPanel.Rounded(ClientRectangle, 22);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        Invalidate(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _lifetime.Dispose();
            _services.Dispose();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var font in _ownedFonts) font.Dispose();
            _fonts.Dispose();
        }
    }
}

internal sealed class GlassPanel : Panel
{
    public GlassPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Rounded(new(1, 1, Width - 3, Height - 3), 14);
        using var glass = new SolidBrush(Color.FromArgb(13, 205, 224, 255));
        using var border = new Pen(Color.FromArgb(38, 202, 221, 255));
        e.Graphics.FillPath(glass, shape);
        e.Graphics.DrawPath(border, shape);
        base.OnPaint(e);
    }

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        var size = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (size <= 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.X, bounds.Y, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Y, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class GlassButton(Color color) : Button
{
    private bool _hover;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(13, 21, 39));
        using var path = GlassPanel.Rounded(new(1, 1, Width - 3, Height - 3), Math.Min(12, Height / 3));
        var fill = !Enabled ? Color.FromArgb(32, 39, 56) : _hover ? ControlPaint.Light(color, .12f) : color;
        using var brush = new LinearGradientBrush(ClientRectangle, ControlPaint.Light(fill, .05f), fill, 90);
        using var border = new Pen(Color.FromArgb(_hover ? 95 : 45, 197, 211, 249));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);
        using var text = new SolidBrush(Enabled ? ForeColor : Color.FromArgb(132, 145, 172));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.DirectionRightToLeft
        };
        e.Graphics.DrawString(Text, Font, text, ClientRectangle, format);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6));
    }
}
