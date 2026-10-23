using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Qingdan.Models;
using Qingdan.Services;
using Qingdan.Views;

namespace Qingdan;

internal static class PreviewRunner
{
    public static async Task RunAsync(App app, string directory)
    {
        Directory.CreateDirectory(directory);
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var data = CreateSampleData();
        var tempStore = new DataStore(Path.Combine(Path.GetTempPath(), "QingdanPreviewData"));
        var state = new AppState(tempStore, new LoadResult(data, null));
        state.Settings.ShowCompletedInWidget = true;

        var main = new MainWindow(app, state)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 30,
            Top = 30
        };

        try
        {
            await ShowAndWaitAsync(main);
            Capture(main, Path.Combine(directory, "main.png"));

            var settings = new SettingsWindow(app, state)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 80,
                Top = 80
            };
            await ShowAndWaitAsync(settings);
            Capture(settings, Path.Combine(directory, "settings.png"));
            settings.Close();

            var editor = new TaskEditorWindow(state, state.Tasks.First(task => task.IsUrgent))
            {
                Owner = main,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await ShowAndWaitAsync(editor);
            Capture(editor, Path.Combine(directory, "task-editor.png"));
            editor.Close();

            var categoryDialog = new CategoryDialogWindow(state)
            {
                Owner = main,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await ShowAndWaitAsync(categoryDialog);
            Capture(categoryDialog, Path.Combine(directory, "category-dialog.png"));
            categoryDialog.Close();

            state.Settings.WidgetSkinId = "mist";
            state.Settings.WidgetBackgroundOpacity = 0.92;
            var widget = new WidgetWindow(app, state)
            {
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            await ShowAndWaitAsync(widget);
            Capture(widget, Path.Combine(directory, "widget-mist.png"));

            state.Settings.WidgetSkinId = "night";
            state.Settings.WidgetBackgroundOpacity = 0.88;
            await PumpAsync();
            Capture(widget, Path.Combine(directory, "widget-night.png"));

            state.Settings.WidgetSkinId = "ocean";
            state.Settings.WidgetBackgroundOpacity = 0.82;
            await PumpAsync();
            Capture(widget, Path.Combine(directory, "widget-ocean.png"));

            app.IsExiting = true;
            widget.Close();
        }
        finally
        {
            app.IsExiting = true;
            main.AllowCloseForExit();
            main.Close();
            state.FlushSave();
        }
    }

    private static async Task ShowAndWaitAsync(Window window)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, RoutedEventArgs args)
        {
            window.Loaded -= Handler;
            loaded.TrySetResult();
        }

        window.Loaded += Handler;
        window.Show();
        await loaded.Task;
        await PumpAsync();
        window.UpdateLayout();
    }

    private static async Task PumpAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(60);
            await Dispatcher.Yield(DispatcherPriority.Render);
        }
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        const double scale = 2.0;
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width * scale)),
            Math.Max(1, (int)Math.Ceiling(height * scale)),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static AppData CreateSampleData()
    {
        var work = new Category { Name = "工作", Color = "#5B7CFA", SortOrder = 0 };
        var life = new Category { Name = "生活", Color = "#F28C63", SortOrder = 1 };
        var study = new Category { Name = "学习", Color = "#2FB98A", SortOrder = 2 };
        var now = DateTime.Now;

        TodoItem Task(string title, Category category, TodoStatus status, TimeSpan age, string notes = "")
        {
            return new TodoItem
            {
                Title = title,
                Notes = notes,
                CategoryId = category.Id,
                Status = status,
                CreatedAt = now - age,
                UpdatedAt = now - age,
                CompletedAt = status == TodoStatus.Done ? now - TimeSpan.FromMinutes(40) : null
            };
        }

        var data = new AppData
        {
            Categories = { work, life, study },
            Tasks =
            {
                Task("今晚 8 点前提交季度预算表", work, TodoStatus.Urgent, TimeSpan.FromHours(3), "核对市场部新增的差旅预算"),
                Task("回复客户关于交付时间的邮件", work, TodoStatus.Urgent, TimeSpan.FromHours(5)),
                Task("整理下周产品评审的要点", work, TodoStatus.Pending, TimeSpan.FromMinutes(30), "把三个待确认的问题先列出来"),
                Task("预约周末的牙医", life, TodoStatus.Pending, TimeSpan.FromHours(2)),
                Task("读完《设计中的设计》第三章", study, TodoStatus.Pending, TimeSpan.FromHours(7)),
                Task("给阳台的绿植换盆", life, TodoStatus.Pending, TimeSpan.FromDays(1)),
                Task("晨间跑步 5 公里", life, TodoStatus.Done, TimeSpan.FromDays(1)),
                Task("整理桌面上的便签", work, TodoStatus.Done, TimeSpan.FromDays(1), "归档到项目文档"),
                Task("买猫粮和猫砂", life, TodoStatus.Done, TimeSpan.FromDays(2))
            },
            Settings = new AppSettings
            {
                ShowWidget = true,
                ShowCompletedInWidget = true,
                WidgetSkinId = "mist",
                WidgetBackgroundOpacity = 0.92
            }
        };

        foreach (var task in data.Tasks)
        {
            var category = data.Categories.FirstOrDefault(item => item.Id == task.CategoryId);
            task.ApplyCategory(category);
        }

        return data;
    }
}
