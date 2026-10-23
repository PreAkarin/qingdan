using System.IO;
using System.Text;
using Microsoft.Win32;
using Qingdan;
using Qingdan.Models;
using Qingdan.Services;

Console.OutputEncoding = Encoding.UTF8;

var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".smoke");
root = Path.GetFullPath(root);
Directory.CreateDirectory(root);

if (args.Length > 0 && args[0] == "installer-test")
{
    return await RunInstallerTestAsync(args);
}

var runDirectory = Path.Combine(root, "run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runDirectory);
var failures = 0;

void Check(bool condition, string message)
{
    if (condition)
    {
        Console.WriteLine("PASS  " + message);
        return;
    }

    failures++;
    Console.Error.WriteLine("FAIL  " + message);
}

try
{
    var store = new DataStore(runDirectory);
    var loaded = store.Load();
    Check(loaded.Warning is null, "首次启动读取没有警告");
    Check(File.Exists(store.DataFilePath), "首次启动会创建 data.json");
    Check(loaded.Data.Categories.Count == 3, "首次启动提供 3 个默认分类");

    var state = new AppState(store, loaded);
    var work = state.Categories.First(category => category.Name == "工作");
    var life = state.Categories.First(category => category.Name == "生活");

    Check(state.TryAddTask("完成设计稿评审", work.Id, TodoStatus.Pending, "带上标注", out var task1, out var error1), "可以新增待办: " + error1);
    Check(task1 is not null && task1.CategoryName == "工作", "新增待办自动应用分类名称");
    Check(state.PendingCount == 1 && state.UrgentCount == 0 && state.DoneCount == 0, "新增待办计入未处理");

    Check(state.TryAddTask("今晚提交报价", work.Id, TodoStatus.Urgent, null, out var task2, out _), "可以新增紧急待办");
    Check(state.TryAddTask("预约牙医", life.Id, TodoStatus.Pending, null, out var task3, out _), "可以新增生活分类待办");
    Check(state.PendingCount == 2 && state.UrgentCount == 1, "状态计数正确");

    state.SetStatus(task1!, TodoStatus.Done);
    Check(state.DoneCount == 1 && state.TodayDoneCount == 1, "标记完成会更新完成计数");
    Check(task1!.CompletedAt is not null, "标记完成会记录完成时间");
    Check(state.DoneTasks.Count == 1 && state.PendingTasks.Count == 1, "看板列会随状态更新");

    state.SetStatus(task1!, TodoStatus.Urgent);
    Check(task1.CompletedAt is null && state.UrgentCount == 2 && state.DoneCount == 0, "从已完成恢复紧急会清空完成时间");

    state.ToggleDone(task2!);
    Check(state.DoneCount == 1 && state.DoneTasks.First().Id == task2!.Id, "紧急任务可直接切换完成");

    state.SelectCategory(work);
    Check(state.IsAllCategoriesSelected == false, "选择分类后取消全选状态");
    Check(state.PendingTasks.All(task => task.CategoryId == work.Id) &&
          state.UrgentTasks.All(task => task.CategoryId == work.Id), "分类筛选作用于看板");

    state.SearchText = "报价";
    Check(state.PendingTasks.Count == 0 && state.UrgentTasks.Count == 0 && state.DoneTasks.Count == 1,
        "搜索可以命中已完成待办");
    state.SearchText = string.Empty;

    state.SetWidgetCategoryFilter(work.Id);
    Check(state.WidgetCategoryFilterId == work.Id, "悬浮窗可选择单个分类");
    Check(state.WidgetUrgentTasks.All(item => item.CategoryId == work.Id), "悬浮窗分类筛选生效");
    state.SetWidgetCategoryFilter(null);
    Check(state.SelectedWidgetCategoryOption?.IsAll == true, "悬浮窗可以选择全部分类（全选）");

    state.Settings.ShowCompletedInWidget = true;
    Check(state.WidgetHasDone, "显示已完成开关会让悬浮窗包含已完成分组");
    state.Settings.ShowCompletedInWidget = false;
    Check(!state.WidgetHasDone && state.WidgetDoneTasks.Count == 0, "关闭后悬浮窗不展示已完成");

    Check(state.TryAddCategory("阅读", "#36A8C7", out var reading, out var categoryError), "可以创建分类: " + categoryError);
    Check(reading is not null && state.ComposerCategoryOptions.Any(option => option.Id == reading!.Id), "新分类进入分类下拉");
    state.SetTaskCategory(task3!, reading!.Id);
    Check(task3!.CategoryName == "阅读", "可以给待办重新归类");

    Check(state.TryUpdateCategory(reading!, "书单", "#7B61E8", out _), "可以重命名分类");
    Check(task3!.CategoryName == "书单", "重命名分类会同步到待办");
    var countBeforeDelete = state.Tasks.Count;
    state.DeleteCategory(reading!);
    Check(state.Tasks.Count == countBeforeDelete && task3.CategoryId is null, "删除分类不会删除待办，只会取消归类");

    var widgetCount = state.WidgetActiveCount;
    state.SelectAllCategories();
    state.FlushSave();
    Check(File.Exists(store.BackupFilePath) || File.Exists(store.DataFilePath), "保存数据成功");

    var reloaded = store.Load();
    Check(reloaded.Warning is null, "再次读取没有警告");
    Check(reloaded.Data.Tasks.Count == 3, "刷新后待办数量保持");
    Check(reloaded.Data.Tasks.Any(item => item.Title == "完成设计稿评审" && item.Status == TodoStatus.Urgent),
        "刷新后状态保持");
    Check(reloaded.Data.Categories.Any(item => item.Name == "书单") == false, "刷新后删除的分类不会复活");

    var corruptDirectory = Path.Combine(runDirectory, "corrupt");
    Directory.CreateDirectory(corruptDirectory);
    File.WriteAllText(Path.Combine(corruptDirectory, "data.json"), "{ this is not json");
    var corruptStore = new DataStore(corruptDirectory);
    var corruptResult = corruptStore.Load();
    Check(corruptResult.Warning is not null, "损坏的数据文件会产生用户提示");
    Check(corruptResult.Data.Categories.Count == 3, "损坏数据会回退到默认分类");
    Check(Directory.EnumerateFiles(corruptDirectory, "data.corrupt-*.json").Any(), "损坏数据会备份");

    Console.WriteLine($"\nRESULT failures={failures} widgetCount={widgetCount}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("EXCEPTION " + ex);
    failures++;
}
finally
{
    try
    {
        if (Directory.Exists(runDirectory))
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }
    catch
    {
        // Ignore cleanup.
    }
}

return failures == 0 ? 0 : 1;

static async Task<int> RunInstallerTestAsync(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: installer-test <published-exe> <target-dir>");
        return 2;
    }

    var sourceExe = Path.GetFullPath(args[1]);
    var target = Path.GetFullPath(args[2]);
    var failures = 0;

    void Check(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine("PASS  " + message);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL  " + message);
    }

    if (!File.Exists(sourceExe))
    {
        Console.Error.WriteLine("missing published exe: " + sourceExe);
        return 2;
    }

    try
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        var service = new InstallationService(sourceExe);
        var result = await service.InstallAsync(target, createDesktopShortcut: true,
            new Progress<InstallProgress>(p => Console.WriteLine($"      {p.Percent:0}% {p.Stage}")));
        Check(result.Success, "安装服务执行成功");
        Check(File.Exists(Path.Combine(target, InstallationService.AppFileName)), "安装目录中存在 Qingdan.exe");
        Check(File.Exists(Path.Combine(target, InstallationService.InstalledInfoFileName)), "写入 install-info.json");
        Check(File.ReadAllBytes(Path.Combine(target, InstallationService.AppFileName)).Length == new FileInfo(sourceExe).Length,
            "安装文件内容完整");

        var startMenuFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), InstallationService.StartMenuFolderName);
        var installedExe = Path.Combine(target, InstallationService.AppFileName);
        var startMenuShortcut = Path.Combine(startMenuFolder, "清单.lnk");
        var startMenuUninstall = Path.Combine(startMenuFolder, "卸载清单.lnk");
        var desktopShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "清单.lnk");
        Check(File.Exists(startMenuShortcut), "创建开始菜单快捷方式");
        Check(File.Exists(startMenuUninstall), "创建开始菜单卸载快捷方式");
        Check(File.Exists(desktopShortcut), "创建桌面快捷方式");

        using (var uninstallKey = Registry.CurrentUser.OpenSubKey(InstallationService.UninstallRegistryPath))
        {
            Check(uninstallKey is not null, "写入应用和功能卸载项");
            Check(uninstallKey?.GetValue("InstallLocation") as string == target, "卸载项记录安装位置");
            Check(uninstallKey?.GetValue("UninstallString") is string uninstallString &&
                  uninstallString.Contains("--uninstall"), "卸载命令包含 --uninstall");
        }

        using (var runKey = Registry.CurrentUser.OpenSubKey(InstallationService.RunRegistryPath))
        {
            var runValue = runKey?.GetValue(InstallationService.RunValueName) as string;
            Check(!string.IsNullOrWhiteSpace(runValue) && runValue.Contains(installedExe), "写入开机启动项");
        }

        if (result.Warnings is { Count: > 0 })
        {
            Console.WriteLine("WARNINGS: " + string.Join(" | ", result.Warnings));
        }

        var installedExe2 = Path.Combine(target, InstallationService.AppFileName);
        var uninstall = await service.UninstallAsync(deleteData: false, executableOverride: installedExe2);
        Check(uninstall.Success, "卸载服务执行成功");
        Check(!Directory.Exists(startMenuFolder) || !File.Exists(startMenuShortcut), "移除开始菜单快捷方式");
        Check(!File.Exists(desktopShortcut), "移除桌面快捷方式");
        Check(Registry.CurrentUser.OpenSubKey(InstallationService.UninstallRegistryPath) is null, "移除应用和功能卸载项");
        using (var runKey = Registry.CurrentUser.OpenSubKey(InstallationService.RunRegistryPath))
        {
            Check(runKey?.GetValue(InstallationService.RunValueName) is null, "移除开机启动项");
        }

        if (uninstall.Warnings is { Count: > 0 })
        {
            Console.WriteLine("UNINSTALL WARNINGS: " + string.Join(" | ", uninstall.Warnings));
        }

        Console.WriteLine("INSTALLER RESULT failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("EXCEPTION " + ex);
        return 1;
    }
}
