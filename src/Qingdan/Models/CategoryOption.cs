using System.Text.Json.Serialization;

namespace Qingdan.Models;

/// <summary>
/// Lightweight item used by category pickers. It intentionally is not persisted.
/// </summary>
public sealed class CategoryOption
{
    public Guid? Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Color { get; init; } = "#AAB3C5";

    public bool IsAll { get; init; }

    public bool IsUncategorized { get; init; }

    [JsonIgnore]
    public string ColorWithAlpha => Color;

    public static CategoryOption CreateAll(string text = "全部分类（全选）") => new()
    {
        Id = null,
        Name = text,
        Color = "#AAB3C5",
        IsAll = true
    };

    public static CategoryOption CreateUncategorized() => new()
    {
        Id = null,
        Name = "未分类",
        Color = "#AAB3C5",
        IsUncategorized = true
    };

    public static CategoryOption FromCategory(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Color = category.Color
    };
}
