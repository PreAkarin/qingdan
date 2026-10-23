using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using Qingdan.Common;
using Qingdan.Models;
using Qingdan.Services;

namespace Qingdan;

public sealed class AppState : ObservableObject
{
    private readonly DataStore _store;
    private readonly DispatcherTimer _saveTimer;
    private readonly List<TodoItem> _tasks;
    private readonly List<Category> _categories;

    private string _searchText = string.Empty;
    private Category? _selectedCategory;
    private string _newTaskTitle = string.Empty;
    private bool _newTaskUrgent;
    private CategoryOption? _newTaskCategoryOption;
    private CategoryOption? _selectedWidgetCategoryOption;
    private bool _initializing = true;
    private string? _lastSaveError;

    public AppState(DataStore store, LoadResult loadResult)
    {
        _store = store;
        _tasks = loadResult.Data.Tasks;
        _categories = loadResult.Data.Categories;
        Settings = loadResult.Data.Settings;
        LoadWarning = loadResult.Warning;

        Tasks = new ObservableCollection<TodoItem>(_tasks);
        Categories = new ObservableCollection<Category>(_categories.OrderBy(category => category.SortOrder));

        PendingTasks = new ObservableCollection<TodoItem>();
        UrgentTasks = new ObservableCollection<TodoItem>();
        DoneTasks = new ObservableCollection<TodoItem>();
        WidgetUrgentTasks = new ObservableCollection<TodoItem>();
        WidgetPendingTasks = new ObservableCollection<TodoItem>();
        WidgetDoneTasks = new ObservableCollection<TodoItem>();
        ComposerCategoryOptions = new ObservableCollection<CategoryOption>();
        EditorCategoryOptions = new ObservableCollection<CategoryOption>();
        WidgetCategoryOptions = new ObservableCollection<CategoryOption>();

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        RebuildCategoryOptions();
        RefreshAll();
        _initializing = false;
    }

    public ObservableCollection<TodoItem> Tasks { get; }

    public ObservableCollection<Category> Categories { get; }

    public ObservableCollection<TodoItem> PendingTasks { get; }

    public ObservableCollection<TodoItem> UrgentTasks { get; }

    public ObservableCollection<TodoItem> DoneTasks { get; }

    public ObservableCollection<TodoItem> WidgetUrgentTasks { get; }

    public ObservableCollection<TodoItem> WidgetPendingTasks { get; }

    public ObservableCollection<TodoItem> WidgetDoneTasks { get; }

    public ObservableCollection<CategoryOption> ComposerCategoryOptions { get; }

    public ObservableCollection<CategoryOption> EditorCategoryOptions { get; }

    public ObservableCollection<CategoryOption> WidgetCategoryOptions { get; }

    public AppSettings Settings { get; }

    public DataStore Store => _store;

    public string? LoadWarning { get; }

    public event Action<string>? SaveFailed;

    public event Action<AppState, string?>? SettingsChanged;

    public event Action? CategoryOptionsChanged;

    public string AppVersion
    {
        get
        {
            var version = typeof(AppState).Assembly.GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 6 => "夜深了",
                < 11 => "早上好",
                < 14 => "中午好",
                < 18 => "下午好",
                _ => "晚上好"
            };
        }
    }

    public string TodayLongDate
    {
        get
        {
            var weekdays = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            var today = DateTime.Today;
            return $"{today:M月d日} {weekdays[(int)today.DayOfWeek]}";
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RefreshMainCollections();
            }
        }
    }

    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                OnPropertiesChanged(nameof(SelectedCategoryId), nameof(IsAllCategoriesSelected), nameof(CurrentBoardTitle));
                if (value is null)
                {
                    SelectComposerCategory(null);
                }
                else
                {
                    SelectComposerCategory(value.Id);
                }

                RefreshMainCollections();
            }
        }
    }

    public Guid? SelectedCategoryId => SelectedCategory?.Id;

    public bool IsAllCategoriesSelected => SelectedCategory is null;

    public string CurrentBoardTitle => SelectedCategory?.Name ?? "全部事项";

    public int TotalCount => _tasks.Count;

    public int ActiveCount => _tasks.Count(task => task.Status != TodoStatus.Done);

    public int PendingCount => _tasks.Count(task => task.Status == TodoStatus.Pending);

    public int UrgentCount => _tasks.Count(task => task.Status == TodoStatus.Urgent);

    public int DoneCount => _tasks.Count(task => task.Status == TodoStatus.Done);

    public int TodayDoneCount => _tasks.Count(task => task.Status == TodoStatus.Done && task.CompletedAt?.Date == DateTime.Today);

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set
        {
            if (SetProperty(ref _newTaskTitle, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanAddNewTask));
            }
        }
    }

    public bool NewTaskUrgent
    {
        get => _newTaskUrgent;
        set => SetProperty(ref _newTaskUrgent, value);
    }

    public CategoryOption? NewTaskCategoryOption
    {
        get => _newTaskCategoryOption;
        set => SetProperty(ref _newTaskCategoryOption, value);
    }

    public bool CanAddNewTask => !string.IsNullOrWhiteSpace(NewTaskTitle);

    public CategoryOption? SelectedWidgetCategoryOption
    {
        get => _selectedWidgetCategoryOption;
        set
        {
            if (SetProperty(ref _selectedWidgetCategoryOption, value))
            {
                OnPropertyChanged(nameof(WidgetScopeLabel));
                RefreshWidgetCollections();
            }
        }
    }

    public Guid? WidgetCategoryFilterId => SelectedWidgetCategoryOption?.Id;

    public string WidgetScopeLabel => SelectedWidgetCategoryOption?.IsAll == true
        ? "全部分类"
        : SelectedWidgetCategoryOption?.Name ?? "全部分类";

    public int WidgetActiveCount => WidgetUrgentTasks.Count + WidgetPendingTasks.Count;

    public int WidgetCompletedTotalCount { get; private set; }

    public bool WidgetHasUrgent => WidgetUrgentTasks.Count > 0;

    public bool WidgetHasPending => WidgetPendingTasks.Count > 0;

    public bool WidgetHasDone => Settings.ShowCompletedInWidget && WidgetDoneTasks.Count > 0;

    public bool WidgetIsEmpty => WidgetUrgentTasks.Count == 0 && WidgetPendingTasks.Count == 0 &&
                                 (!Settings.ShowCompletedInWidget || WidgetDoneTasks.Count == 0);

    public bool WidgetHasActive => WidgetActiveCount > 0;

    public string WidgetFooterSummary => WidgetActiveCount == 0 ? "今天的事都完成了" : $"{WidgetActiveCount} 件待办";

    public bool ShowWidget => Settings.ShowWidget;

    public bool ShowCompletedInWidget => Settings.ShowCompletedInWidget;

    public int VisibleCount => PendingTasks.Count + UrgentTasks.Count + DoneTasks.Count;

    public void SelectCategory(Category? category)
    {
        SelectedCategory = category;
        OnPropertiesChanged(nameof(IsAllCategoriesSelected));
    }

    public void SelectAllCategories() => SelectCategory(null);

    public bool TryAddFromComposer(out string? error)
    {
        var success = TryAddTask(
            NewTaskTitle,
            NewTaskCategoryOption?.Id,
            NewTaskUrgent ? TodoStatus.Urgent : TodoStatus.Pending,
            string.Empty,
            out var created,
            out error);

        if (success)
        {
            NewTaskTitle = string.Empty;
            NewTaskUrgent = false;
            OnPropertyChanged(nameof(CanAddNewTask));
            _ = created;
        }

        return success;
    }

    public bool TryAddTask(
        string? title,
        Guid? categoryId,
        TodoStatus status,
        string? notes,
        out TodoItem? created,
        out string? error)
    {
        created = null;
        error = null;
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            error = "先写下要做的事吧。";
            return false;
        }

        if (normalizedTitle.Length > 200)
        {
            error = "待办内容最多 200 个字符。";
            return false;
        }

        if (status == TodoStatus.Done)
        {
            status = TodoStatus.Pending;
        }

        Guid? normalizedCategoryId = null;
        if (categoryId is { } requestedCategory && Categories.Any(category => category.Id == requestedCategory))
        {
            normalizedCategoryId = requestedCategory;
        }

        var now = DateTime.Now;
        created = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = normalizedTitle,
            Notes = (notes ?? string.Empty).Trim(),
            CategoryId = normalizedCategoryId,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = _tasks.Count
        };
        created.ApplyCategory(FindCategory(created.CategoryId));

        _tasks.Insert(0, created);
        Tasks.Insert(0, created);
        RefreshAll();
        ScheduleSave();
        return true;
    }

    public bool TryAddWidgetTask(string? title, bool urgent, out string? error)
    {
        var categoryId = WidgetCategoryFilterId;
        var success = TryAddTask(
            title,
            categoryId,
            urgent ? TodoStatus.Urgent : TodoStatus.Pending,
            string.Empty,
            out _,
            out error);
        return success;
    }

    public void SetStatus(TodoItem task, TodoStatus status)
    {
        if (task.Status == status)
        {
            return;
        }

        task.Status = status;
        if (status == TodoStatus.Done)
        {
            task.CompletedAt = DateTime.Now;
        }
        else
        {
            task.CompletedAt = null;
        }

        task.Touch();
        RefreshAll();
        ScheduleSave();
    }

    public void ToggleDone(TodoItem task)
    {
        SetStatus(task, task.Status == TodoStatus.Done ? TodoStatus.Pending : TodoStatus.Done);
    }

    public void SetTaskCategory(TodoItem task, Guid? categoryId)
    {
        Guid? normalized = null;
        if (categoryId is { } requested && Categories.Any(category => category.Id == requested))
        {
            normalized = requested;
        }

        if (task.CategoryId == normalized)
        {
            return;
        }

        task.CategoryId = normalized;
        task.ApplyCategory(FindCategory(normalized));
        task.Touch();
        RefreshAll();
        ScheduleSave();
    }

    public void UpdateTask(TodoItem task, string title, string notes, Guid? categoryId, TodoStatus status)
    {
        var normalizedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return;
        }

        task.Title = normalizedTitle;
        task.Notes = (notes ?? string.Empty).Trim();
        task.CategoryId = categoryId is { } id && Categories.Any(category => category.Id == id) ? id : null;
        task.ApplyCategory(FindCategory(task.CategoryId));
        if (task.Status != status)
        {
            task.Status = status;
            task.CompletedAt = status == TodoStatus.Done ? DateTime.Now : null;
        }
        else if (status == TodoStatus.Done && task.CompletedAt is null)
        {
            task.CompletedAt = DateTime.Now;
        }
        else if (status != TodoStatus.Done)
        {
            task.CompletedAt = null;
        }

        task.Touch();
        RefreshAll();
        ScheduleSave();
    }

    public void DeleteTask(TodoItem task)
    {
        Tasks.Remove(task);
        _tasks.Remove(task);
        RefreshAll();
        ScheduleSave();
    }

    public bool TryAddCategory(string name, string color, out Category? created, out string? error)
    {
        created = null;
        error = null;
        var normalizedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            error = "分类名称不能为空。";
            return false;
        }

        if (normalizedName.Length > 16)
        {
            error = "分类名称最多 16 个字符。";
            return false;
        }

        if (Categories.Any(category => string.Equals(category.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            error = "已经存在同名分类了。";
            return false;
        }

        created = new Category
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Color = DataStore.NormalizeColor(color),
            SortOrder = Categories.Count,
            TotalCount = 0,
            ActiveCount = 0
        };
        Categories.Add(created);
        _categories.Add(created);
        RebuildCategoryOptions();
        RefreshAll();
        ScheduleSave();
        return true;
    }

    public bool TryUpdateCategory(Category category, string name, string color, out string? error)
    {
        error = null;
        var normalizedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            error = "分类名称不能为空。";
            return false;
        }

        if (normalizedName.Length > 16)
        {
            error = "分类名称最多 16 个字符。";
            return false;
        }

        if (Categories.Any(other => other.Id != category.Id &&
                                    string.Equals(other.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase)))
        {
            error = "已经存在同名分类了。";
            return false;
        }

        category.Name = normalizedName;
        category.Color = DataStore.NormalizeColor(color);
        RebuildCategoryOptions();
        RefreshAll();
        ScheduleSave();
        return true;
    }

    public void DeleteCategory(Category category)
    {
        foreach (var task in _tasks.Where(task => task.CategoryId == category.Id))
        {
            task.CategoryId = null;
            task.ApplyCategory(null);
        }

        Categories.Remove(category);
        _categories.Remove(category);
        if (SelectedCategory?.Id == category.Id)
        {
            SelectedCategory = null;
        }

        RebuildCategoryOptions();
        RefreshAll();
        ScheduleSave();
    }

    public Category? FindCategory(Guid? id)
    {
        if (id is null)
        {
            return null;
        }

        return Categories.FirstOrDefault(category => category.Id == id);
    }

    public void SetWidgetCategoryFilter(Guid? categoryId)
    {
        if (categoryId is null)
        {
            SelectedWidgetCategoryOption = WidgetCategoryOptions.FirstOrDefault(option => option.IsAll);
            return;
        }

        SelectedWidgetCategoryOption = WidgetCategoryOptions.FirstOrDefault(option => option.Id == categoryId)
                                       ?? WidgetCategoryOptions.FirstOrDefault(option => option.IsAll);
    }

    public void RefreshWidgetCategoryFilter()
    {
        var currentId = SelectedWidgetCategoryOption?.Id;
        if (currentId is null)
        {
            SelectedWidgetCategoryOption = WidgetCategoryOptions.FirstOrDefault(option => option.IsAll);
        }
        else
        {
            SelectedWidgetCategoryOption = WidgetCategoryOptions.FirstOrDefault(option => option.Id == currentId)
                                           ?? WidgetCategoryOptions.FirstOrDefault(option => option.IsAll);
        }
    }

    private void SelectComposerCategory(Guid? categoryId)
    {
        NewTaskCategoryOption = ComposerCategoryOptions.FirstOrDefault(option =>
                                  categoryId is null
                                      ? option.IsUncategorized
                                      : option.Id == categoryId)
                              ?? ComposerCategoryOptions.FirstOrDefault(option => option.IsUncategorized);
    }

    private void RebuildCategoryOptions()
    {
        var previousComposerId = NewTaskCategoryOption?.IsUncategorized == true ? null : NewTaskCategoryOption?.Id;
        var preferredWidgetId = SelectedWidgetCategoryOption?.IsAll == true ? null : SelectedWidgetCategoryOption?.Id;

        ComposerCategoryOptions.Clear();
        ComposerCategoryOptions.Add(CategoryOption.CreateUncategorized());
        EditorCategoryOptions.Clear();
        EditorCategoryOptions.Add(CategoryOption.CreateUncategorized());
        WidgetCategoryOptions.Clear();
        WidgetCategoryOptions.Add(CategoryOption.CreateAll());

        foreach (var category in Categories.OrderBy(category => category.SortOrder).ThenBy(category => category.Name))
        {
            var option = CategoryOption.FromCategory(category);
            ComposerCategoryOptions.Add(option);
            EditorCategoryOptions.Add(option);
            WidgetCategoryOptions.Add(option);
        }

        NewTaskCategoryOption = previousComposerId is null
            ? ComposerCategoryOptions.FirstOrDefault()
            : ComposerCategoryOptions.FirstOrDefault(option => option.Id == previousComposerId) ??
              ComposerCategoryOptions.FirstOrDefault();

        _selectedWidgetCategoryOption = preferredWidgetId is null
            ? WidgetCategoryOptions.FirstOrDefault(option => option.IsAll)
            : WidgetCategoryOptions.FirstOrDefault(option => option.Id == preferredWidgetId) ??
              WidgetCategoryOptions.FirstOrDefault(option => option.IsAll);
        OnPropertyChanged(nameof(SelectedWidgetCategoryOption));
        OnPropertyChanged(nameof(WidgetScopeLabel));

        CategoryOptionsChanged?.Invoke();
    }

    private void RefreshAll()
    {
        foreach (var category in Categories)
        {
            category.ActiveCount = _tasks.Count(task => task.CategoryId == category.Id && task.Status != TodoStatus.Done);
            category.TotalCount = _tasks.Count(task => task.CategoryId == category.Id);
        }

        foreach (var task in _tasks)
        {
            task.ApplyCategory(FindCategory(task.CategoryId));
        }

        RefreshMainCollections();
        RefreshWidgetCollections();
        OnPropertiesChanged(
            nameof(TotalCount),
            nameof(ActiveCount),
            nameof(PendingCount),
            nameof(UrgentCount),
            nameof(DoneCount),
            nameof(TodayDoneCount),
            nameof(VisibleCount));
    }

    private void RefreshMainCollections()
    {
        var query = _tasks.AsEnumerable();
        if (SelectedCategory is { } selected)
        {
            query = query.Where(task => task.CategoryId == selected.Id);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim().ToLowerInvariant();
            query = query.Where(task => task.SearchText.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList();
        Replace(
            PendingTasks,
            filtered.Where(task => task.Status == TodoStatus.Pending)
                .OrderByDescending(task => task.CreatedAt)
                .ThenByDescending(task => task.SortOrder));
        Replace(
            UrgentTasks,
            filtered.Where(task => task.Status == TodoStatus.Urgent)
                .OrderByDescending(task => task.CreatedAt)
                .ThenByDescending(task => task.SortOrder));
        Replace(
            DoneTasks,
            filtered.Where(task => task.Status == TodoStatus.Done)
                .OrderByDescending(task => task.CompletedAt ?? task.UpdatedAt)
                .ThenByDescending(task => task.SortOrder));
        OnPropertyChanged(nameof(VisibleCount));
    }

    private void RefreshWidgetCollections()
    {
        var categoryId = WidgetCategoryFilterId;
        var query = _tasks.AsEnumerable();
        if (categoryId is { } selectedId)
        {
            query = query.Where(task => task.CategoryId == selectedId);
        }

        var filtered = query.ToList();
        Replace(
            WidgetUrgentTasks,
            filtered.Where(task => task.Status == TodoStatus.Urgent)
                .OrderByDescending(task => task.CreatedAt)
                .ThenByDescending(task => task.SortOrder));
        Replace(
            WidgetPendingTasks,
            filtered.Where(task => task.Status == TodoStatus.Pending)
                .OrderByDescending(task => task.CreatedAt)
                .ThenByDescending(task => task.SortOrder));

        WidgetCompletedTotalCount = filtered.Count(task => task.Status == TodoStatus.Done);

        if (Settings.ShowCompletedInWidget)
        {
            Replace(
                WidgetDoneTasks,
                filtered.Where(task => task.Status == TodoStatus.Done)
                    .OrderByDescending(task => task.CompletedAt ?? task.UpdatedAt)
                    .ThenByDescending(task => task.SortOrder));
        }
        else
        {
            WidgetDoneTasks.Clear();
        }

        OnPropertiesChanged(
            nameof(WidgetActiveCount),
            nameof(WidgetHasUrgent),
            nameof(WidgetHasPending),
            nameof(WidgetHasDone),
            nameof(WidgetIsEmpty),
            nameof(WidgetHasActive),
            nameof(WidgetFooterSummary),
            nameof(WidgetCompletedTotalCount),
            nameof(ShowCompletedInWidget));
    }

    private static void Replace(ObservableCollection<TodoItem> target, IEnumerable<TodoItem> items)
    {
        var list = items.ToList();
        if (target.Count == list.Count && target.SequenceEqual(list))
        {
            return;
        }

        target.Clear();
        foreach (var item in list)
        {
            target.Add(item);
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.ShowCompletedInWidget))
        {
            RefreshWidgetCollections();
        }

        if (e.PropertyName == nameof(AppSettings.ShowWidget))
        {
            OnPropertyChanged(nameof(ShowWidget));
        }

        OnPropertyChanged(nameof(ShowCompletedInWidget));
        SettingsChanged?.Invoke(this, e.PropertyName);
        ScheduleSave();
    }

    public void NotifyWidgetVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowWidget));
    }

    public void ScheduleSave()
    {
        if (_initializing)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void FlushSave()
    {
        _saveTimer.Stop();
        SaveNow();
    }

    private void SaveNow()
    {
        try
        {
            var data = new AppData
            {
                SchemaVersion = 1,
                Tasks = _tasks.ToList(),
                Categories = Categories.OrderBy(category => category.SortOrder).ToList(),
                Settings = Settings
            };
            _store.Save(data);
            _lastSaveError = null;
        }
        catch (Exception ex)
        {
            var message = $"保存清单失败：{ex.Message}";
            if (!string.Equals(message, _lastSaveError, StringComparison.Ordinal))
            {
                _lastSaveError = message;
                SaveFailed?.Invoke(message);
            }
        }
    }
}
