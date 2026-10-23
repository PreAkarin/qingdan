using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Qingdan.Models;

namespace Qingdan.Views;

public partial class WidgetWindow : Window
{
    private readonly App _app;
    private readonly AppState _state;
    private readonly DispatcherTimer _positionSaveTimer;
    private bool _positionReady;
    private bool _quickAddOpen;

    public WidgetWindow(App app, AppState state)
    {
        _app = app;
        _state = state;
        InitializeComponent();
        DataContext = state;

        _positionSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(420)
        };
        _positionSaveTimer.Tick += (_, _) =>
        {
            _positionSaveTimer.Stop();
            SavePosition();
        };

        RestorePosition();
        ApplySkin();
        _state.Settings.PropertyChanged += OnSettingsPropertyChanged;
        LocationChanged += OnLocationChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClampPositionToVirtualScreen();
        ApplySkin();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _positionSaveTimer.Stop();
        SavePosition();
        _state.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        LocationChanged -= OnLocationChanged;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePosition();
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            _state.Settings.ShowWidget = false;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.WidgetSkinId) or nameof(AppSettings.WidgetBackgroundOpacity))
        {
            ApplySkin();
        }
    }

    private void ApplySkin()
    {
        var skin = WidgetSkins.Get(_state.Settings.WidgetSkinId);
        var opacity = Math.Clamp(_state.Settings.WidgetBackgroundOpacity, 0.35, 1.0);
        var backgroundAlpha = ToAlpha(opacity);

        var gradient = new LinearGradientBrush(
            WithAlpha(skin.Color(skin.BackgroundStart), backgroundAlpha),
            WithAlpha(skin.Color(skin.BackgroundEnd), backgroundAlpha),
            new Point(0, 0),
            new Point(1, 1));
        gradient.Freeze();
        Resources["WidgetWindowBackgroundBrush"] = gradient;

        Resources["WidgetTextBrush"] = Brush(skin.Color(skin.TextPrimary));
        Resources["WidgetTextSecondaryBrush"] = Brush(skin.Color(skin.TextSecondary));
        Resources["WidgetMutedBrush"] = Brush(skin.Color(skin.TextMuted));
        Resources["WidgetBorderBrush"] = Brush(WithAlpha(skin.Color(skin.Border), ToAlpha(0.62 * opacity + 0.18)));
        Resources["WidgetSurfaceBrush"] = Brush(WithAlpha(skin.Color(skin.Surface), ToAlpha(0.50 + 0.32 * opacity)));
        Resources["WidgetPopupBackgroundBrush"] = Brush(WithAlpha(skin.Color(skin.Surface), 248));
        Resources["WidgetInputBrush"] = Brush(WithAlpha(skin.Color(skin.Surface), ToAlpha(0.50 + 0.28 * opacity)));
        Resources["WidgetDividerBrush"] = Brush(WithAlpha(skin.Color(skin.Border), ToAlpha(0.70 * opacity)));
        Resources["WidgetAccentBrush"] = Brush(skin.Color(skin.Accent));
        Resources["WidgetAccentSoftBrush"] = Brush(WithAlpha(skin.Color(skin.Accent), 36));
        Resources["WidgetDangerBrush"] = Brush(skin.Color(skin.Danger));
        Resources["WidgetDangerSoftBrush"] = Brush(WithAlpha(skin.Color(skin.Danger), 32));
        Resources["WidgetDoneBrush"] = Brush(skin.Color(skin.Done));
        Resources["WidgetDoneSoftBrush"] = Brush(WithAlpha(skin.Color(skin.Done), 32));
        Resources["WidgetCheckRingBrush"] = Brush(WithAlpha(skin.Color(skin.TextMuted), 185));

        if (skin.IsDark)
        {
            Resources["WidgetHoverBrush"] = Brush(Color.FromArgb(26, 255, 255, 255));
        }
        else
        {
            Resources["WidgetHoverBrush"] = Brush(Color.FromArgb(20, 20, 28, 48));
        }

        var iconGradient = new LinearGradientBrush(
            skin.Color(skin.Accent),
            skin.Color(skin.AccentSecondary),
            new Point(0, 0),
            new Point(1, 1));
        iconGradient.Freeze();
        Resources["WidgetIconTileBrush"] = iconGradient;

        WidgetShell.Effect = new DropShadowEffect
        {
            Color = skin.Color(skin.Shadow),
            BlurRadius = 34,
            ShadowDepth = 9,
            Direction = 270,
            Opacity = skin.IsDark ? 0.36 : 0.22
        };
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static byte ToAlpha(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    }

    private void RestorePosition()
    {
        _positionReady = false;
        var workArea = SystemParameters.WorkArea;
        var left = _state.Settings.WidgetLeft ?? workArea.Right - Width - 28;
        var top = _state.Settings.WidgetTop ?? workArea.Bottom - Height - 20;

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        Left = Math.Clamp(left, virtualLeft + 8, Math.Max(virtualLeft + 8, virtualRight - Width - 8));
        Top = Math.Clamp(top, virtualTop + 8, Math.Max(virtualTop + 8, virtualBottom - Height - 8));
        _positionReady = true;
    }

    private void ClampPositionToVirtualScreen()
    {
        if (!_positionReady)
        {
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        var nextLeft = Math.Clamp(Left, virtualLeft + 8, Math.Max(virtualLeft + 8, virtualRight - ActualWidth - 8));
        var nextTop = Math.Clamp(Top, virtualTop + 8, Math.Max(virtualTop + 8, virtualBottom - ActualHeight - 8));
        if (Math.Abs(nextLeft - Left) > 0.5 || Math.Abs(nextTop - Top) > 0.5)
        {
            Left = nextLeft;
            Top = nextTop;
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_positionReady)
        {
            return;
        }

        _positionSaveTimer.Stop();
        _positionSaveTimer.Start();
    }

    private void SavePosition()
    {
        if (!_positionReady || WindowState != WindowState.Normal)
        {
            return;
        }

        if (_state.Settings.WidgetLeft is null || Math.Abs(_state.Settings.WidgetLeft.Value - Left) > 0.5)
        {
            _state.Settings.WidgetLeft = Left;
        }

        if (_state.Settings.WidgetTop is null || Math.Abs(_state.Settings.WidgetTop.Value - Top) > 0.5)
        {
            _state.Settings.WidgetTop = Top;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore fast click releases.
        }
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        return FindAncestor<ButtonBase>(source) is not null ||
               FindAncestor<TextBoxBase>(source) is not null ||
               FindAncestor<ComboBox>(source) is not null ||
               FindAncestor<ScrollBar>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }

    private void ToggleQuickAdd_Click(object sender, RoutedEventArgs e)
    {
        SetQuickAddOpen(!_quickAddOpen);
    }

    private void SetQuickAddOpen(bool open)
    {
        _quickAddOpen = open;
        QuickAddPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        QuickAddGlyph.Text = open ? "\uE711" : "\uE710";
        QuickAddToggleButton.ToolTip = open ? "收起快速新增" : "快速新增待办";

        if (open)
        {
            Dispatcher.BeginInvoke(() =>
            {
                QuickAddInput.Focus();
                Keyboard.Focus(QuickAddInput);
            }, DispatcherPriority.Input);
        }
        else
        {
            QuickAddInput.Clear();
        }
    }

    private void QuickAddInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitQuickAdd();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SetQuickAddOpen(false);
        }
    }

    private void QuickAddSubmit_Click(object sender, RoutedEventArgs e)
    {
        SubmitQuickAdd();
    }

    private void SubmitQuickAdd()
    {
        var title = QuickAddInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            SetQuickAddOpen(false);
            return;
        }

        if (_state.TryAddWidgetTask(title, urgent: false, out var error))
        {
            QuickAddInput.Clear();
            WidgetScroll.ScrollToTop();
            QuickAddInput.Focus();
            Keyboard.Focus(QuickAddInput);
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "清单", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _quickAddOpen && !QuickAddInput.IsKeyboardFocusWithin)
        {
            SetQuickAddOpen(false);
        }
    }

    private void TaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem task)
        {
            _state.ToggleDone(task);
            e.Handled = true;
        }
    }

    private void TaskRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2)
        {
            return;
        }

        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null ||
            FindAncestor<TextBoxBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is TodoItem task)
        {
            OpenEditor(task);
            e.Handled = true;
        }
    }

    private void MenuMarkPending_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskFromMenu(sender) is { } task)
        {
            _state.SetStatus(task, TodoStatus.Pending);
        }
    }

    private void MenuMarkUrgent_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskFromMenu(sender) is { } task)
        {
            _state.SetStatus(task, TodoStatus.Urgent);
        }
    }

    private void MenuMarkDone_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskFromMenu(sender) is { } task)
        {
            _state.SetStatus(task, TodoStatus.Done);
        }
    }

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskFromMenu(sender) is { } task)
        {
            OpenEditor(task);
        }
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskFromMenu(sender) is not { } task)
        {
            return;
        }

        if (ConfirmDialog.Ask(this, "删除待办", $"确定删除“{task.Title}”吗？此操作无法撤销。", "删除"))
        {
            _state.DeleteTask(task);
        }
    }

    private static TodoItem? GetTaskFromMenu(object sender)
    {
        if (sender is not MenuItem menuItem)
        {
            return null;
        }

        if (menuItem.DataContext is TodoItem task)
        {
            return task;
        }

        return menuItem.Parent is ContextMenu contextMenu
            ? (contextMenu.PlacementTarget as FrameworkElement)?.DataContext as TodoItem
            : null;
    }

    private void OpenEditor(TodoItem task)
    {
        var editor = new TaskEditorWindow(_state, task) { Owner = this };
        editor.ShowDialog();
    }

    private void OpenMain_Click(object sender, RoutedEventArgs e) => _app.ShowMain();

    private void OpenQuickAdd_Click(object sender, RoutedEventArgs e) => SetQuickAddOpen(true);

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => _app.ShowSettings();

    private void HideWidget_Click(object sender, RoutedEventArgs e)
    {
        SetQuickAddOpen(false);
        _state.Settings.ShowWidget = false;
    }
}
