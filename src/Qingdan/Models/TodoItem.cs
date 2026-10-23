using System.Text.Json.Serialization;
using Qingdan.Common;

namespace Qingdan.Models;

public sealed class TodoItem : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private Guid? _categoryId;
    private TodoStatus _status = TodoStatus.Pending;
    private DateTime _createdAt = DateTime.Now;
    private DateTime? _completedAt;
    private DateTime _updatedAt = DateTime.Now;
    private int _sortOrder;
    private string _categoryName = "未分类";
    private string _categoryColor = "#AAB3C5";

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value ?? string.Empty))
            {
                OnPropertiesChanged(nameof(HasNotes), nameof(NotesPreview), nameof(SearchText));
            }
        }
    }

    public Guid? CategoryId
    {
        get => _categoryId;
        set => SetProperty(ref _categoryId, value);
    }

    public TodoStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertiesChanged(nameof(IsPending), nameof(IsUrgent), nameof(IsDone), nameof(StatusLabel),
                    nameof(StatusEnglish), nameof(TimeDisplay));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set
        {
            if (SetProperty(ref _createdAt, value))
            {
                OnPropertiesChanged(nameof(CreatedDisplay), nameof(TimeDisplay));
            }
        }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (SetProperty(ref _completedAt, value))
            {
                OnPropertiesChanged(nameof(CompletedDisplay), nameof(TimeDisplay));
            }
        }
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    [JsonIgnore]
    public bool IsPending => Status == TodoStatus.Pending;

    [JsonIgnore]
    public bool IsUrgent => Status == TodoStatus.Urgent;

    [JsonIgnore]
    public bool IsDone => Status == TodoStatus.Done;

    [JsonIgnore]
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    [JsonIgnore]
    public string NotesPreview => Notes.Replace("\r", " ").Replace("\n", " ").Trim();

    [JsonIgnore]
    public string StatusLabel => Status switch
    {
        TodoStatus.Urgent => "紧急",
        TodoStatus.Done => "已完成",
        _ => "未处理"
    };

    [JsonIgnore]
    public string StatusEnglish => Status switch
    {
        TodoStatus.Urgent => "URGENT",
        TodoStatus.Done => "DONE",
        _ => "TO DO"
    };

    [JsonIgnore]
    public string CategoryName
    {
        get => _categoryName;
        private set => SetProperty(ref _categoryName, value);
    }

    [JsonIgnore]
    public string CategoryColor
    {
        get => _categoryColor;
        private set => SetProperty(ref _categoryColor, value);
    }

    [JsonIgnore]
    public bool HasCategory => CategoryId.HasValue;

    [JsonIgnore]
    public string CreatedDisplay
    {
        get
        {
            var today = DateTime.Today;
            if (CreatedAt.Date == today)
            {
                return $"今天 {CreatedAt:HH:mm}";
            }

            if (CreatedAt.Date == today.AddDays(-1))
            {
                return $"昨天 {CreatedAt:HH:mm}";
            }

            return CreatedAt.Year == today.Year ? CreatedAt.ToString("M月d日 HH:mm") : CreatedAt.ToString("yyyy年M月d日");
        }
    }

    [JsonIgnore]
    public string CompletedDisplay
    {
        get
        {
            if (CompletedAt is null)
            {
                return string.Empty;
            }

            return CompletedAt.Value.Date == DateTime.Today
                ? $"今天 {CompletedAt.Value:HH:mm} 完成"
                : $"{CompletedAt.Value:M月d日 HH:mm} 完成";
        }
    }

    [JsonIgnore]
    public string TimeDisplay => IsDone ? CompletedDisplay : CreatedDisplay;

    [JsonIgnore]
    public string SearchText => $"{Title} {Notes} {CategoryName}".ToLowerInvariant();

    public void ApplyCategory(Category? category)
    {
        var name = category?.Name ?? "未分类";
        var color = category?.Color ?? "#AAB3C5";
        if (CategoryName != name)
        {
            CategoryName = name;
            OnPropertyChanged(nameof(SearchText));
        }

        if (CategoryColor != color)
        {
            CategoryColor = color;
        }

        OnPropertyChanged(nameof(HasCategory));
    }

    public void Touch()
    {
        UpdatedAt = DateTime.Now;
    }
}
