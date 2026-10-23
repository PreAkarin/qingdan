using System.IO;
using Microsoft.Win32;

namespace Qingdan.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Qingdan";

    public string? ExecutablePath
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return null;
            }

            // During development the app can be hosted by "dotnet". Never register the
            // generic dotnet host as a startup item.
            var fileName = Path.GetFileNameWithoutExtension(processPath);
            if (string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return processPath;
        }
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetCommand(out string? command)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            command = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(command);
        }
        catch
        {
            command = null;
            return false;
        }
    }

    public bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "无法打开 Windows 启动项注册表。";
                return false;
            }

            if (!enabled)
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                return true;
            }

            var exePath = ExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                error = "当前运行环境不支持设置开机自启。安装版或免安装版启动后可正常使用。";
                return false;
            }

            var command = $"\"{exePath}\" --autostart";
            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            error = $"设置开机自启失败：{ex.Message}";
            return false;
        }
    }

    public void ApplyOnLaunch(bool enabled)
    {
        // Re-point an existing startup entry at the current executable whenever the app runs.
        // This makes moving a portable copy or updating an installed copy behave correctly.
        if (enabled)
        {
            TryGetCommand(out var existing);
            var exePath = ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var desired = $"\"{exePath}\" --autostart";
                if (!string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase))
                {
                    SetEnabled(true, out _);
                }
            }
        }
        else if (TryGetCommand(out _))
        {
            SetEnabled(false, out _);
        }
    }
}
