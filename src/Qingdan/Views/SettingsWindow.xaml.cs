using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Qingdan.Models;

namespace Qingdan.Views;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly AppState _state;

    public SettingsWindow(App app, AppState state)
    {
        _app = app;
        _state = state;
        InitializeComponent();
        DataContext = state;
        SkinList.ItemsSource = WidgetSkins.All;
        Height = Math.Max(MinHeight, Math.Min(760, SystemParameters.WorkArea.Height - 36));
        Width = Math.Max(MinWidth, Math.Min(780, SystemParameters.WorkArea.Width - 36));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore quick releases.
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        var guard = 0;
        while (current is not null && guard++ < 32)
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

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _state.Store.OpenDataDirectory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法打开数据文件夹：{ex.Message}", "清单", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _state.Settings.WidgetSkinId = "mist";
        _state.Settings.WidgetBackgroundOpacity = 0.9;
        _state.Settings.ShowCompletedInWidget = false;
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        _app.ExitApp();
    }
}
