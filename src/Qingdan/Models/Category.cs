using System.Text.Json.Serialization;
using Qingdan.Common;

namespace Qingdan.Models;

public sealed class Category : ObservableObject
{
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private string _color = "#5B7CFA";
    private int _sortOrder;
    private int _activeCount;
    private int _totalCount;

    public Guid Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value ?? "#5B7CFA");
    }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    [JsonIgnore]
    public int ActiveCount
    {
        get => _activeCount;
        internal set
        {
            if (SetProperty(ref _activeCount, value))
            {
                OnPropertyChanged(nameof(CountDisplay));
            }
        }
    }

    [JsonIgnore]
    public int TotalCount
    {
        get => _totalCount;
        internal set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(CountDisplay));
            }
        }
    }

    [JsonIgnore]
    public string CountDisplay => ActiveCount > 0 ? ActiveCount.ToString() : string.Empty;
}
