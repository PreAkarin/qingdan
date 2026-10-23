using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Qingdan.Services;

namespace Qingdan.Views;

public partial class InstallerWindow : Window
{
    private readonly InstallerLaunchMode _mode;
    private readonly InstallationService _service = new();
    private bool _busy;
    private bool _launchAfterInstall;
    private InstallResult? _installResult;

    public InstallerWindow(InstallerLaunchMode mode)
    {
        _mode = mode;
        InitializeComponent();
        VersionText.Text = $"版本 {InstallationService.ProductVersion}";
        HeaderTitle.Text = mode == InstallerLaunchMode.Install ? "安装 清单" : "卸载 清单";
        HeaderSubtitle.Text = mode == InstallerLaunchMode.Install ? "QINGDAN SETUP" : "QINGDAN UNINSTALL";
        Title = mode == InstallerLaunchMode.Install ? "安装 清单" : "卸载 清单";

        if (mode == InstallerLaunchMode.Install)
        {
            InstallFormPanel.Visibility = Visibility.Visible;
            UninstallFormPanel.Visibility = Visibility.Collapsed;
            InstallPathInput.Text = InstallationService.DefaultInstallDirectory;
            DataPathHint.Text = $"清单数据保存在 {InstallationService.DataDirectory}，升级或重装不会覆盖已有待办。";
        }
        else
        {
            InstallFormPanel.Visibility = Visibility.Collapsed;
            UninstallFormPanel.Visibility = Visibility.Visible;
            UninstallPathText.Text = InstallationService.CurrentInstallDirectory;
            UninstallDataPathText.Text = InstallationService.DataDirectory;
            SuccessTitle.Text = "卸载完成";
        }

        Loaded += (_, _) =>
        {
            if (mode == InstallerLaunchMode.Install)
            {
                InstallPathInput.Focus();
                InstallPathInput.SelectAll();
            }
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var initialDirectory = Directory.Exists(InstallPathInput.Text)
            ? InstallPathInput.Text
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dialog = new OpenFolderDialog
        {
            Title = "选择清单的安装位置",
            InitialDirectory = initialDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            InstallPathInput.Text = dialog.FolderName;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        InstallErrorText.Visibility = Visibility.Collapsed;
        var destination = InstallPathInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            ShowInstallError("请选择安装位置。");
            return;
        }

        SetBusy(true);
        SetProgressMode("正在安装清单", "正在准备安装…", "E896");
        _launchAfterInstall = OpenAfterInstallCheck.IsChecked == true;

        var progress = new Progress<InstallProgress>(UpdateProgress);
        var result = await _service.InstallAsync(destination, DesktopShortcutCheck.IsChecked == true, progress);
        if (!result.Success)
        {
            SetBusy(false);
            ShowInstallError(result.Message);
            return;
        }

        _installResult = result;
        ShowSuccess(
            "安装完成",
            "清单已经准备好陪你了。可以从桌面或开始菜单打开。",
            result.InstallDirectory is null ? string.Empty : $"安装位置：{result.InstallDirectory}",
            result.Warnings,
            "完成");
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var deleteData = DeleteDataCheck.IsChecked == true;
        var message = deleteData
            ? "将卸载清单并永久删除所有分类、待办和设置。此操作无法撤销，是否继续？"
            : "将卸载清单程序、快捷方式和开机启动项。你的清单数据会保留在本机，是否继续？";
        if (!ConfirmDialog.Ask(this, "卸载清单", message, "卸载清单"))
        {
            return;
        }

        UninstallErrorText.Visibility = Visibility.Collapsed;
        SetBusy(true);
        SetProgressMode("正在卸载清单", "正在关闭正在运行的清单…", "E74D");

        var progress = new Progress<InstallProgress>(UpdateProgress);
        var result = await _service.UninstallAsync(deleteData, progress);
        if (!result.Success)
        {
            SetBusy(false);
            ShowUninstallError(result.Message);
            return;
        }

        var successMessage = result.DataRemoved
            ? "清单程序和所有数据已从这台电脑移除。"
            : "应用已移除，你的待办数据仍保留在本机，重新安装后可以继续使用。";
        ShowSuccess("卸载完成", successMessage, "应用文件会在窗口关闭后自动清理。", result.Warnings, "关闭");
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == InstallerLaunchMode.Install && _installResult is { Success: true } result &&
            _launchAfterInstall && result.InstallDirectory is not null)
        {
            try
            {
                InstallationService.LaunchInstalledApp(
                    Path.Combine(result.InstallDirectory, InstallationService.AppFileName),
                    result.InstallDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"启动清单失败：{ex.Message}\n\n请从桌面或开始菜单手动打开。",
                    "清单", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        Close();
    }

    private void SetProgressMode(string title, string stage, string glyph)
    {
        FormPanelsToProgress();
        ProgressTitle.Text = title;
        ProgressStage.Text = stage;
        ProgressGlyph.Text = char.ConvertFromUtf32(Convert.ToInt32(glyph, 16));
        InstallProgressBar.Value = 0;
        ProgressPercent.Text = "0%";
    }

    private void UpdateProgress(InstallProgress progress)
    {
        InstallProgressBar.Value = Math.Clamp(progress.Percent, 0, 100);
        ProgressStage.Text = progress.Stage;
        ProgressPercent.Text = $"{Math.Round(progress.Percent)}%";
    }

    private void FormPanelsToProgress()
    {
        InstallFormPanel.Visibility = Visibility.Collapsed;
        UninstallFormPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
    }

    private void ShowSuccess(string title, string message, string path, IReadOnlyList<string>? warnings, string buttonText)
    {
        SetBusy(false);
        InstallFormPanel.Visibility = Visibility.Collapsed;
        UninstallFormPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Visible;
        SuccessTitle.Text = title;
        SuccessMessage.Text = message;
        SuccessPathText.Text = path;
        FinishButton.Content = buttonText;
        if (warnings is { Count: > 0 })
        {
            WarningsList.ItemsSource = warnings;
            WarningsList.Visibility = Visibility.Visible;
        }
        else
        {
            WarningsList.Visibility = Visibility.Collapsed;
        }

        FinishButton.Focus();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CloseButton.IsEnabled = !busy;
    }

    private void ShowInstallError(string message)
    {
        InstallFormPanel.Visibility = Visibility.Visible;
        UninstallFormPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        InstallErrorText.Text = message;
        InstallErrorText.Visibility = Visibility.Visible;
    }

    private void ShowUninstallError(string message)
    {
        InstallFormPanel.Visibility = Visibility.Collapsed;
        UninstallFormPanel.Visibility = Visibility.Visible;
        ProgressPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        UninstallErrorText.Text = message;
        UninstallErrorText.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy)
        {
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_busy)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore fast releases.
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
}
