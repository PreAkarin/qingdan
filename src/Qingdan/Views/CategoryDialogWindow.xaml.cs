using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Qingdan.Models;

namespace Qingdan.Views;

public partial class CategoryDialogWindow : Window
{
    private readonly AppState _state;
    private readonly Category? _editingCategory;

    public CategoryDialogWindow(AppState state, Category? category = null)
    {
        _state = state;
        _editingCategory = category;
        Colors = new ObservableCollection<string>
        {
            "#5B7CFA",
            "#7B61E8",
            "#D95F9A",
            "#F28C63",
            "#E3A72F",
            "#2FB98A",
            "#36A8C7",
            "#8B94A7"
        };

        InitializeComponent();
        DataContext = this;

        if (category is null)
        {
            ColorList.SelectedItem = Colors[0];
            Loaded += (_, _) =>
            {
                NameInput.Focus();
                Keyboard.Focus(NameInput);
            };
        }
        else
        {
            DialogTitle.Text = "编辑分类";
            DialogSubtitle.Text = "调整名称或颜色，关联的待办会一起更新";
            SaveButton.Content = "保存修改";
            NameInput.Text = category.Name;
            ColorList.SelectedItem = Colors.Contains(category.Color) ? category.Color : Colors[0];
            Loaded += (_, _) =>
            {
                NameInput.Focus();
                NameInput.SelectAll();
            };
        }
    }

    public ObservableCollection<string> Colors { get; }

    public Category? CreatedOrUpdatedCategory { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var selectedColor = ColorList.SelectedItem as string ?? Colors[0];
        var name = NameInput.Text;

        bool success;
        string? error;
        Category? category;
        if (_editingCategory is null)
        {
            success = _state.TryAddCategory(name, selectedColor, out category, out error);
        }
        else
        {
            success = _state.TryUpdateCategory(_editingCategory, name, selectedColor, out error);
            category = _editingCategory;
        }

        if (!success)
        {
            ErrorText.Text = error ?? "无法保存分类。";
            ErrorText.Visibility = Visibility.Visible;
            NameInput.Focus();
            return;
        }

        CreatedOrUpdatedCategory = category;
        DialogResult = true;
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
