using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Qingdan.Services;

public enum InstallerLaunchMode
{
    Install,
    Uninstall
}

public sealed record InstallProgress(double Percent, string Stage);

public sealed record InstallResult(bool Success, string Message, string? InstallDirectory = null,
    IReadOnlyList<string>? Warnings = null);

public sealed record UninstallResult(bool Success, string Message, bool DataRemoved,
    IReadOnlyList<string>? Warnings = null);

public sealed class InstallationService
{
    public const string AppFileName = "清单.exe";
    public const string InstalledInfoFileName = "install-info.json";
    public const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Qingdan";
    public const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "Qingdan";
    public const string StartMenuFolderName = "清单";

    private readonly string _sourceExecutable;

    public InstallationService(string? sourceExecutable = null)
    {
        _sourceExecutable = sourceExecutable ?? Environment.ProcessPath ?? string.Empty;
    }

    public static string ProductVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "清单");

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qingdan");

    public string CurrentExecutable => _sourceExecutable;

    public static string CurrentInstallDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty;

    public static InstallerLaunchMode? DetectLaunchMode(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            return InstallerLaunchMode.Uninstall;
        }

        if (args.Any(arg => string.Equals(arg, "--install", StringComparison.OrdinalIgnoreCase)))
        {
            return InstallerLaunchMode.Install;
        }

        var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (executableName.Contains("安装", StringComparison.CurrentCultureIgnoreCase) ||
            executableName.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
            executableName.Contains("Installer", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerLaunchMode.Install;
        }

        return null;
    }

    public async Task<InstallResult> InstallAsync(
        string destinationDirectory,
        bool createDesktopShortcut,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        string? temporaryFile = null;

        try
        {
            if (string.IsNullOrWhiteSpace(_sourceExecutable) || !File.Exists(_sourceExecutable))
            {
                return new InstallResult(false, "找不到安装源文件，请重新下载安装程序。");
            }

            var destination = ValidateAndNormalizeDestination(destinationDirectory);
            var destinationExe = Path.Combine(destination, AppFileName);
            var source = Path.GetFullPath(_sourceExecutable);
            if (string.Equals(source, destinationExe, StringComparison.OrdinalIgnoreCase))
            {
                // Installing in place. Keep the current executable.
            }
            else
            {
                progress?.Report(new InstallProgress(3, "正在准备安装位置…"));
                Directory.CreateDirectory(destination);

                await TryQuitRunningAppAsync(destinationExe, cancellationToken).ConfigureAwait(true);

                temporaryFile = Path.Combine(destination, AppFileName + ".installing");
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }

                await CopyWithProgressAsync(source, temporaryFile, progress, cancellationToken).ConfigureAwait(true);

                try
                {
                    if (File.Exists(destinationExe))
                    {
                        try
                        {
                            File.Replace(temporaryFile, destinationExe, null, ignoreMetadataErrors: true);
                        }
                        catch (Exception replaceException) when (replaceException is IOException or PlatformNotSupportedException)
                        {
                            File.Copy(temporaryFile, destinationExe, overwrite: true);
                            File.Delete(temporaryFile);
                        }
                    }
                    else
                    {
                        File.Move(temporaryFile, destinationExe);
                    }
                }
                catch (IOException ex)
                {
                    return new InstallResult(false,
                        $"无法替换目标文件，可能清单仍在运行。请先从托盘退出清单，然后重试。\n\n{ex.Message}");
                }
                finally
                {
                    if (File.Exists(temporaryFile))
                    {
                        try
                        {
                            File.Delete(temporaryFile);
                        }
                        catch
                        {
                            // Ignore.
                        }
                    }
                }
            }

            temporaryFile = null;
            progress?.Report(new InstallProgress(68, "正在写入安装信息…"));

            var installInfo = new InstallInfo
            {
                Version = ProductVersion,
                InstallDirectory = destination,
                InstalledAt = DateTimeOffset.Now
            };
            try
            {
                var json = JsonSerializer.Serialize(installInfo, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(destination, InstalledInfoFileName), json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                warnings.Add($"写入安装信息失败：{ex.Message}");
            }

            progress?.Report(new InstallProgress(74, "正在创建快捷方式…"));
            try
            {
                CreateStartMenuShortcuts(destinationExe, destination);
            }
            catch (Exception ex)
            {
                warnings.Add($"创建开始菜单快捷方式失败：{ex.Message}");
            }

            try
            {
                CreateDesktopShortcutIfRequested(destinationExe, destination, createDesktopShortcut);
            }
            catch (Exception ex)
            {
                warnings.Add($"创建桌面快捷方式失败：{ex.Message}");
            }

            progress?.Report(new InstallProgress(83, "正在注册卸载信息…"));
            try
            {
                RegisterUninstallEntry(destinationExe, destination);
            }
            catch (Exception ex)
            {
                warnings.Add($"写入“应用和功能”卸载信息失败：{ex.Message}");
            }

            try
            {
                SetStartupEntry(destinationExe, enabled: true);
            }
            catch (Exception ex)
            {
                warnings.Add($"设置开机自启失败：{ex.Message}");
            }

            progress?.Report(new InstallProgress(100, "安装完成"));
            return new InstallResult(true, "清单已安装完成。", destination, warnings);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "安装已取消。");
        }
        catch (Exception ex)
        {
            return new InstallResult(false, $"安装失败：{ex.Message}");
        }
        finally
        {
            if (temporaryFile is not null && File.Exists(temporaryFile))
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch
                {
                    // Ignore cleanup failures.
                }
            }
        }
    }

    public async Task<UninstallResult> UninstallAsync(
        bool deleteData,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? executableOverride = null)
    {
        var warnings = new List<string>();
        var executable = Path.GetFullPath(executableOverride ?? Environment.ProcessPath ?? string.Empty);
        var installDirectory = Path.GetDirectoryName(executable) ?? string.Empty;

        try
        {
            progress?.Report(new InstallProgress(8, "正在关闭清单…"));
            await TryQuitRunningAppAsync(executable, cancellationToken).ConfigureAwait(true);

            progress?.Report(new InstallProgress(30, "正在移除快捷方式…"));
            try
            {
                RemoveShortcuts();
            }
            catch (Exception ex)
            {
                warnings.Add($"删除快捷方式失败：{ex.Message}");
            }

            progress?.Report(new InstallProgress(48, "正在清理注册表…"));
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                warnings.Add($"删除卸载注册表项失败：{ex.Message}");
            }

            try
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
                runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                warnings.Add($"删除开机启动项失败：{ex.Message}");
            }

            var dataRemoved = false;
            if (deleteData)
            {
                progress?.Report(new InstallProgress(62, "正在删除清单数据…"));
                try
                {
                    if (Directory.Exists(DataDirectory))
                    {
                        Directory.Delete(DataDirectory, recursive: true);
                    }

                    dataRemoved = true;
                }
                catch (Exception ex)
                {
                    warnings.Add($"删除清单数据失败：{ex.Message}");
                }
            }

            try
            {
                var infoPath = Path.Combine(installDirectory, InstalledInfoFileName);
                if (File.Exists(infoPath))
                {
                    File.Delete(infoPath);
                }
            }
            catch
            {
                // The cleanup script will try again after the process exits.
            }

            progress?.Report(new InstallProgress(78, "正在安排清理应用文件…"));
            try
            {
                ScheduleSelfCleanup(installDirectory, executable);
            }
            catch (Exception ex)
            {
                warnings.Add($"清理应用文件失败，请手动删除“{installDirectory}”。{ex.Message}");
            }

            progress?.Report(new InstallProgress(100, "卸载完成"));
            return new UninstallResult(true, "清单已卸载。", dataRemoved, warnings);
        }
        catch (OperationCanceledException)
        {
            return new UninstallResult(false, "卸载已取消。", false);
        }
        catch (Exception ex)
        {
            return new UninstallResult(false, $"卸载失败：{ex.Message}", false);
        }
    }

    public static void SetStartupEntry(string executablePath, bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true)
                           ?? throw new IOException("无法打开开机启动注册表项。");
        if (enabled)
        {
            runKey.SetValue(RunValueName, $"\"{executablePath}\" --autostart", RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    public static void LaunchInstalledApp(string executablePath, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
    }

    private async Task CopyWithProgressAsync(string source, string destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024;
        var sourceInfo = new FileInfo(source);
        var total = Math.Max(1L, sourceInfo.Length);
        var copied = 0L;

        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[bufferSize];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(true);
            if (read <= 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
            copied += read;
            var percent = 5 + copied * 58.0 / total;
            progress?.Report(new InstallProgress(percent, "正在复制清单程序…"));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(true);
    }

    private static async Task TryQuitRunningAppAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--quit",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    // The other instance may still be shutting down.
                }
            }
        }
        catch
        {
            // If no installed app exists or policy blocks process launch, continue.
        }

        await Task.Delay(800, cancellationToken).ConfigureAwait(true);
    }

    private static string ValidateAndNormalizeDestination(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("请选择安装位置。");
        }

        var destination = Path.GetFullPath(destinationDirectory.Trim().Trim('"'));
        var root = Path.GetPathRoot(destination);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(destination.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("不能直接安装到磁盘根目录，请选择一个文件夹。");
        }

        if (destination.Length > 220)
        {
            throw new ArgumentException("安装路径过长，请选择更短的路径。");
        }

        return destination;
    }

    private static void CreateStartMenuShortcuts(string executablePath, string installDirectory)
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var folder = Path.Combine(startMenu, StartMenuFolderName);
        Directory.CreateDirectory(folder);
        CreateShortcut(Path.Combine(folder, "清单.lnk"), executablePath, string.Empty, installDirectory,
            "清单 · 待办与桌面悬浮窗");
        CreateShortcut(Path.Combine(folder, "卸载清单.lnk"), executablePath, "--uninstall", installDirectory,
            "卸载清单");
    }

    private static void CreateDesktopShortcutIfRequested(string executablePath, string installDirectory, bool create)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "清单.lnk");
        if (create)
        {
            CreateShortcut(shortcutPath, executablePath, string.Empty, installDirectory, "清单 · 待办与桌面悬浮窗");
        }
        else if (File.Exists(shortcutPath))
        {
            try
            {
                File.Delete(shortcutPath);
            }
            catch
            {
                // Ignore.
            }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string arguments,
        string workingDirectory, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("系统不支持创建快捷方式（WScript.Shell 不可用）。");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                throw new InvalidOperationException("无法启动快捷方式服务。");
            }

            dynamic wsh = shell;
            shortcut = wsh.CreateShortcut(shortcutPath);
            dynamic link = shortcut;
            link.TargetPath = targetPath;
            link.Arguments = arguments;
            link.WorkingDirectory = workingDirectory;
            link.IconLocation = targetPath + ",0";
            link.Description = description;
            link.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void RemoveShortcuts()
    {
        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "清单.lnk");
        if (File.Exists(desktopShortcut))
        {
            File.Delete(desktopShortcut);
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), StartMenuFolderName);
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore individual shortcuts.
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }

    private static void RegisterUninstallEntry(string executablePath, string installDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath, writable: true)
                        ?? throw new IOException("无法创建卸载注册表项。");

        var sizeKb = 0L;
        try
        {
            sizeKb = new FileInfo(executablePath).Length / 1024;
        }
        catch
        {
            // Ignore.
        }

        key.SetValue("DisplayName", "清单", RegistryValueKind.String);
        key.SetValue("DisplayVersion", ProductVersion, RegistryValueKind.String);
        key.SetValue("Publisher", "清单", RegistryValueKind.String);
        key.SetValue("DisplayIcon", $"\"{executablePath}\",0", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{executablePath}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{executablePath}\" --uninstall", RegistryValueKind.String);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), RegistryValueKind.String);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void ScheduleSelfCleanup(string installDirectory, string executablePath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "qingdan-uninstall-" + Guid.NewGuid().ToString("N") + ".ps1");
        var pid = Environment.ProcessId;
        var escapedDirectory = EscapeSingleQuoted(installDirectory);
        var escapedExecutable = EscapeSingleQuoted(executablePath);
        var escapedInfo = EscapeSingleQuoted(Path.Combine(installDirectory, InstalledInfoFileName));
        var escapedScript = EscapeSingleQuoted(scriptPath);

        var script = $$"""
                      $ErrorActionPreference = 'SilentlyContinue'
                      $count = 0
                      while ($count -lt 240) {
                          $proc = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
                          if ($null -eq $proc) { break }
                          Start-Sleep -Milliseconds 500
                          $count++
                      }
                      Start-Sleep -Milliseconds 700
                      Remove-Item -LiteralPath '{{escapedExecutable}}' -Force -ErrorAction SilentlyContinue
                      Remove-Item -LiteralPath '{{escapedInfo}}' -Force -ErrorAction SilentlyContinue
                      try {
                          $entries = @(Get-ChildItem -LiteralPath '{{escapedDirectory}}' -Force -ErrorAction SilentlyContinue)
                          if ($entries.Count -eq 0) {
                              Remove-Item -LiteralPath '{{escapedDirectory}}' -Force -ErrorAction SilentlyContinue
                          }
                      } catch {}
                      Remove-Item -LiteralPath '{{escapedScript}}' -Force -ErrorAction SilentlyContinue
                      """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string EscapeSingleQuoted(string value) => value.Replace("'", "''");

    private sealed class InstallInfo
    {
        public string Version { get; set; } = ProductVersion;

        public string InstallDirectory { get; set; } = string.Empty;

        public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;
    }
}
