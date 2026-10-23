using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Qingdan.Services;

public sealed class TrayService : IDisposable
{
    private readonly App _app;
    private readonly AppState _state;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _icon;
    private readonly Forms.ToolStripMenuItem _showWidgetItem;
    private readonly Forms.ToolStripMenuItem _showDoneItem;
    private readonly Forms.ToolStripMenuItem _autoStartItem;
    private readonly TrayMessageWindow? _messageWindow;
    private bool _disposed;

    public TrayService(App app, AppState state)
    {
        _app = app;
        _state = state;
        _icon = LoadApplicationIcon();

        _menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new TrayColorTable()),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Drawing.Font("Microsoft YaHei UI", 9.2f, Drawing.FontStyle.Regular),
            Padding = new Forms.Padding(4, 6, 4, 6),
            BackColor = Drawing.Color.White,
            ForeColor = Drawing.ColorTranslator.FromHtml("#1A2036"),
            RenderMode = Forms.ToolStripRenderMode.Professional
        };

        var openItem = new Forms.ToolStripMenuItem("打开清单")
        {
            Font = new Drawing.Font("Microsoft YaHei UI", 9.2f, Drawing.FontStyle.Bold)
        };
        openItem.Click += (_, _) => _app.ShowMain();
        _menu.Items.Add(openItem);

        var quickAddItem = new Forms.ToolStripMenuItem("快速新增待办");
        quickAddItem.Click += (_, _) => _app.ShowMain(focusComposer: true);
        _menu.Items.Add(quickAddItem);

        _menu.Items.Add(new Forms.ToolStripSeparator());

        _showWidgetItem = new Forms.ToolStripMenuItem("显示桌面悬浮窗");
        _showWidgetItem.Click += (_, _) =>
        {
            _state.Settings.ShowWidget = !_state.Settings.ShowWidget;
        };
        _menu.Items.Add(_showWidgetItem);

        _showDoneItem = new Forms.ToolStripMenuItem("悬浮窗显示已完成");
        _showDoneItem.Click += (_, _) =>
        {
            _state.Settings.ShowCompletedInWidget = !_state.Settings.ShowCompletedInWidget;
        };
        _menu.Items.Add(_showDoneItem);

        _menu.Items.Add(new Forms.ToolStripSeparator());

        var settingsItem = new Forms.ToolStripMenuItem("设置与外观");
        settingsItem.Click += (_, _) => _app.ShowSettings();
        _menu.Items.Add(settingsItem);

        _autoStartItem = new Forms.ToolStripMenuItem("开机自动启动");
        _autoStartItem.Click += (_, _) =>
        {
            _state.Settings.AutoStart = !_state.Settings.AutoStart;
        };
        _menu.Items.Add(_autoStartItem);

        _menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("退出清单");
        exitItem.Click += (_, _) => _app.ExitApp();
        _menu.Items.Add(exitItem);

        _menu.Opening += (_, _) => RefreshMenuState();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "清单 · 待办与桌面悬浮窗",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                _app.ShowMain();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => _app.ShowMain();
        _notifyIcon.BalloonTipClicked += (_, _) => _app.ShowMain();

        RefreshMenuState();

        try
        {
            _messageWindow = new TrayMessageWindow(this);
        }
        catch
        {
            _messageWindow = null;
        }
    }

    public void RefreshMenuState()
    {
        if (_disposed)
        {
            return;
        }

        _showWidgetItem.Checked = _state.Settings.ShowWidget;
        _showDoneItem.Checked = _state.Settings.ShowCompletedInWidget;
        _showDoneItem.Enabled = _state.Settings.ShowWidget;
        _autoStartItem.Checked = _state.Settings.AutoStart;
    }

    public void ShowBalloon(string title, string text, bool warning = false)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(3500);
        }
        catch
        {
            // Balloon notifications are optional.
        }
    }

    internal void RestoreIconAfterExplorerRestart()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
            _notifyIcon.Icon = _icon;
            _notifyIcon.Text = "清单 · 待办与桌面悬浮窗";
            _notifyIcon.ContextMenuStrip = _menu;
        }
        catch
        {
            // Ignore; clicking the tray area again usually re-creates it.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch
        {
            // Ignore.
        }

        _messageWindow?.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Drawing.Icon(stream);
            }
        }
        catch
        {
            // Fall back to the default icon below.
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private sealed class TrayColorTable : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color BrandSoft = Drawing.ColorTranslator.FromHtml("#EEF1FF");
        private static readonly Drawing.Color Brand = Drawing.ColorTranslator.FromHtml("#5669F5");
        private static readonly Drawing.Color Border = Drawing.ColorTranslator.FromHtml("#E7EAF2");

        public override Drawing.Color MenuItemSelected => BrandSoft;
        public override Drawing.Color MenuItemSelectedGradientBegin => BrandSoft;
        public override Drawing.Color MenuItemSelectedGradientEnd => BrandSoft;
        public override Drawing.Color MenuItemBorder => BrandSoft;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.White;
        public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.White;
        public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.White;
        public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.White;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Border;
        public override Drawing.Color CheckBackground => Drawing.Color.White;
        public override Drawing.Color CheckSelectedBackground => BrandSoft;
        public override Drawing.Color ButtonSelectedHighlight => Brand;
    }

    private sealed class TrayMessageWindow : Forms.NativeWindow, IDisposable
    {
        private const int WM_TASKBAR_CREATED = 0x0000;
        private readonly TrayService _owner;
        private readonly int _taskbarCreatedMessage;

        public TrayMessageWindow(TrayService owner)
        {
            _owner = owner;
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

            CreateHandle(new Forms.CreateParams
            {
                Caption = "Qingdan.Tray.MessageWindow",
                Parent = IntPtr.Zero,
                Style = 0,
                ExStyle = 0x00000080,
                X = -32000,
                Y = -32000,
                Width = 0,
                Height = 0
            });
        }

        protected override void WndProc(ref Forms.Message m)
        {
            if (_taskbarCreatedMessage != 0 && m.Msg == _taskbarCreatedMessage)
            {
                _owner.RestoreIconAfterExplorerRestart();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try
            {
                DestroyHandle();
            }
            catch
            {
                // Ignore.
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string lpString);
    }
}
