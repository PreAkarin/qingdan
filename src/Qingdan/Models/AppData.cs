namespace Qingdan.Models;

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;

    public List<TodoItem> Tasks { get; set; } = new();

    public List<Category> Categories { get; set; } = new();

    public AppSettings Settings { get; set; } = new();

    public static AppData CreateFirstRun()
    {
        var data = new AppData
        {
            Categories =
            {
                new Category { Name = "工作", Color = "#5B7CFA", SortOrder = 0 },
                new Category { Name = "生活", Color = "#F28C63", SortOrder = 1 },
                new Category { Name = "学习", Color = "#2FB98A", SortOrder = 2 }
            },
            Settings = new AppSettings()
        };

        return data;
    }
}
