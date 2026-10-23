using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Qingdan.Models;

namespace Qingdan.Views;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly AppState _state;
    private bool _closingForExit;

    public MainWindow(App app, AppState state)
    {
        _app = app;
        _state = state;
        InitializeComponent();
        DataContext = state;

        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closing += OnClosing;
        Closed += (_, _) => _state.PropertyChanged -= OnStatePropertyChanged;
        _state.PropertyChanged += OnStatePropertyChanged;
    }

    public void FocusComposer()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            ComposerInput.Focus();
            Keyboard.Focus(ComposerInput);
            ComposerInput.CaretIndex = ComposerInput.Text.Length;
        }, DispatcherPriority.Input);
    }

    public void FocusSearch()
    {
        Dispatcher.BeginInvoke(() =>
        {
            SearchTextBox.Focus();
            Keyboard.Focus(SearchTextBox);
            SearchTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    public void PrepareForShow(bool focusComposer)
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        if (focusComposer)
        {
            FocusComposer();
        }
    }

    public void AllowCloseForExit()
    {
        _closingForExit = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableRoundedCorners();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizeGlyph();
        if (!string.IsNullOrWhiteSpace(_state.LoadWarning))
        {
            _app.ReportLoadWarning(_state.LoadWarning);
        }
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppState.SelectedCategory))
        {
            CategoryList.SelectedItem = _state.SelectedCategory;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_closingForExit && !_app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _app.NotifyMainHidden();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if the button is released very quickly.
            }
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

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeGlyph is not null)
        {
            MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _app.NotifyMainHidden();
    }

    private void AllTasksButton_Click(object sender, RoutedEventArgs e)
    {
        _state.SelectAllCategories();
        CategoryList.SelectedItem = null;
    }

    private void AddCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CategoryDialogWindow(_state) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CreatedOrUpdatedCategory is { } category)
        {
            _state.SelectCategory(category);
            CategoryList.ScrollIntoView(category);
        }
    }

    private void MenuRenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (GetCategoryFromMenu(sender) is not { } category)
        {
            return;
        }

        var dialog = new CategoryDialogWindow(_state, category) { Owner = this };
        dialog.ShowDialog();
    }

    private void MenuDeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (GetCategoryFromMenu(sender) is not { } category)
        {
            return;
        }

        var count = _state.Tasks.Count(task => task.CategoryId == category.Id);
        var message = count > 0
            ? $"“{category.Name}”下还有 {count} 项待办。删除分类后，这些待办会保留并变为“未分类”。"
            : $"确定要删除分类“{category.Name}”吗？";
        if (ConfirmDialog.Ask(this, "删除分类", message, "删除分类"))
        {
            _state.DeleteCategory(category);
        }
    }

    private static Category? GetCategoryFromMenu(object sender)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is Category category)
        {
            return category;
        }

        if (sender is MenuItem item && item.Parent is ContextMenu contextMenu &&
            (contextMenu.PlacementTarget as FrameworkElement)?.DataContext is Category placementCategory)
        {
            return placementCategory;
        }

        return null;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => _app.ShowSettings();

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        _state.SearchText = string.Empty;
        SearchTextBox.Clear();
        SearchTextBox.Focus();
    }

    private void ComposerInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddFromComposer();
        }
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e) => AddFromComposer();

    private void AddFromComposer()
    {
        if (!_state.CanAddNewTask)
        {
            ComposerInput.Focus();
            return;
        }

        if (_state.TryAddFromComposer(out _))
        {
            ComposerInput.Focus();
            Keyboard.Focus(ComposerInput);
        }
    }

    private void AddPendingButton_Click(object sender, RoutedEventArgs e)
    {
        _state.NewTaskUrgent = false;
        FocusComposer();
    }

    private void AddUrgentButton_Click(object sender, RoutedEventArgs e)
    {
        _state.NewTaskUrgent = true;
        FocusComposer();
    }

    private void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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
            OpenTaskEditor(task);
            e.Handled = true;
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

    private void TaskMore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TodoItem task)
        {
            OpenTaskEditor(task);
        }
    }

    private void MenuEditTask_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskFromMenu(sender) is { } task)
        {
            OpenTaskEditor(task);
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

    private void MenuDeleteTask_Click(object sender, RoutedEventArgs e)
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

    private void OpenTaskEditor(TodoItem task)
    {
        var editor = new TaskEditorWindow(_state, task) { Owner = this };
        editor.ShowDialog();
    }

    private void TryEnableRoundedCorners()
    {
        if (Environment.OSVersion.Version.Build < 22000)
        {
            return;
        }

        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Older Windows versions or compositor policies can reject this.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
