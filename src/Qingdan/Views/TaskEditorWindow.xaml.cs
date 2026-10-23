using System.Windows;
using System.Windows.Input;
using Qingdan.Models;

namespace Qingdan.Views;

public partial class TaskEditorWindow : Window
{
    private readonly AppState _state;
    private readonly TodoItem _task;

    public TaskEditorWindow(AppState state, TodoItem task)
    {
        _state = state;
        _task = task;
        InitializeComponent();

        TitleInput.Text = task.Title;
        NotesInput.Text = task.Notes;
        CategoryCombo.ItemsSource = state.EditorCategoryOptions;
        CategoryCombo.SelectedItem = state.EditorCategoryOptions.FirstOrDefault(option =>
            task.CategoryId is null ? option.IsUncategorized : option.Id == task.CategoryId);
        if (CategoryCombo.SelectedItem is null)
        {
            CategoryCombo.SelectedIndex = 0;
        }

        switch (task.Status)
        {
            case TodoStatus.Urgent:
                UrgentRadio.IsChecked = true;
                break;
            case TodoStatus.Done:
                DoneRadio.IsChecked = true;
                break;
            default:
                PendingRadio.IsChecked = true;
                break;
        }

        MetaText.Text = task.IsDone
            ? $"创建于 {task.CreatedDisplay} · {task.CompletedDisplay}"
            : $"创建于 {task.CreatedDisplay}";

        Loaded += (_, _) =>
        {
            TitleInput.Focus();
            TitleInput.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ErrorText.Text = "待办内容不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            TitleInput.Focus();
            return;
        }

        var option = CategoryCombo.SelectedItem as CategoryOption;
        var categoryId = option is null || option.IsUncategorized ? null : option.Id;
        var status = UrgentRadio.IsChecked == true
            ? TodoStatus.Urgent
            : DoneRadio.IsChecked == true
                ? TodoStatus.Done
                : TodoStatus.Pending;

        _state.UpdateTask(_task, title, NotesInput.Text, categoryId, status);
        DialogResult = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDialog.Ask(this, "删除待办", $"确定删除“{_task.Title}”吗？此操作无法撤销。", "删除"))
        {
            _state.DeleteTask(_task);
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore fast click releases.
            }
        }
    }
}
