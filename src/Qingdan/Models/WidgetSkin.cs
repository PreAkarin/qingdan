using System.Windows.Media;

namespace Qingdan.Models;

public sealed class WidgetSkin
{
    public string Id { get; init; } = "mist";

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string BackgroundStart { get; init; } = "#F9FBFF";

    public string BackgroundEnd { get; init; } = "#E9EFFB";

    public string TextPrimary { get; init; } = "#172039";

    public string TextSecondary { get; init; } = "#5C6780";

    public string TextMuted { get; init; } = "#8A94A8";

    public string Accent { get; init; } = "#5268EF";

    public string AccentSecondary { get; init; } = "#7A5CF0";

    public string Border { get; init; } = "#C8D2E8";

    public string Surface { get; init; } = "#FFFFFF";

    public string Shadow { get; init; } = "#1A2340";

    public string Danger { get; init; } = "#D94A42";

    public string Done { get; init; } = "#1E9A71";

    public bool IsDark { get; init; }

    public Brush BackgroundPreview => new LinearGradientBrush(
        Parse(BackgroundStart),
        Parse(BackgroundEnd),
        new System.Windows.Point(0, 0),
        new System.Windows.Point(1, 1));

    public Brush AccentPreview => new SolidColorBrush(Parse(Accent));

    public Brush TextPreview => new SolidColorBrush(Parse(TextPrimary));

    public Color Color(string value) => Parse(value);

    public static Color Parse(string value)
    {
        return (Color)ColorConverter.ConvertFromString(value);
    }
}

public static class WidgetSkins
{
    public static IReadOnlyList<WidgetSkin> All { get; } = new List<WidgetSkin>
    {
        new()
        {
            Id = "mist",
            Name = "晨雾白",
            Description = "轻盈的白色玻璃",
            BackgroundStart = "#FAFCFF",
            BackgroundEnd = "#E7EEFC",
            TextPrimary = "#172039",
            TextSecondary = "#5C6780",
            TextMuted = "#8A94A8",
            Accent = "#5268EF",
            AccentSecondary = "#7A5CF0",
            Border = "#C9D4EA",
            Surface = "#FFFFFF",
            Shadow = "#1A2340",
            Danger = "#D94A42",
            Done = "#1E9A71"
        },
        new()
        {
            Id = "night",
            Name = "深空黑",
            Description = "安静专注的夜色",
            BackgroundStart = "#181E30",
            BackgroundEnd = "#252C42",
            TextPrimary = "#F2F4FF",
            TextSecondary = "#BAC2D7",
            TextMuted = "#858FAB",
            Accent = "#91A1FF",
            AccentSecondary = "#B59BFF",
            Border = "#3A445C",
            Surface = "#1F2639",
            Shadow = "#000000",
            Danger = "#FF9185",
            Done = "#77DDB5",
            IsDark = true
        },
        new()
        {
            Id = "ocean",
            Name = "远山蓝",
            Description = "清透的晨间湖面",
            BackgroundStart = "#E9F4FF",
            BackgroundEnd = "#D0E3F7",
            TextPrimary = "#102A43",
            TextSecondary = "#49637C",
            TextMuted = "#7D93A8",
            Accent = "#2F7EC7",
            AccentSecondary = "#57A5DA",
            Border = "#A9C9E5",
            Surface = "#F5FAFF",
            Shadow = "#173A5E",
            Danger = "#D44C4C",
            Done = "#218368"
        },
        new()
        {
            Id = "mint",
            Name = "薄荷绿",
            Description = "清爽的呼吸感",
            BackgroundStart = "#E9FAF3",
            BackgroundEnd = "#CFF0E3",
            TextPrimary = "#14332B",
            TextSecondary = "#4D6A61",
            TextMuted = "#80978F",
            Accent = "#1F9D78",
            AccentSecondary = "#55C5A0",
            Border = "#A9DAC8",
            Surface = "#F5FFFB",
            Shadow = "#173B30",
            Danger = "#D1544E",
            Done = "#218368"
        },
        new()
        {
            Id = "peach",
            Name = "蜜桃粉",
            Description = "柔软的日落温度",
            BackgroundStart = "#FFF2ED",
            BackgroundEnd = "#FFE0E9",
            TextPrimary = "#3F2026",
            TextSecondary = "#76545A",
            TextMuted = "#A08589",
            Accent = "#E36B7E",
            AccentSecondary = "#F0A07C",
            Border = "#EFC4C5",
            Surface = "#FFF9F7",
            Shadow = "#4A2027",
            Danger = "#D94A42",
            Done = "#3D9278"
        },
        new()
        {
            Id = "sand",
            Name = "燕麦棕",
            Description = "温和的纸页质感",
            BackgroundStart = "#FFF9EC",
            BackgroundEnd = "#F1E1C5",
            TextPrimary = "#392F20",
            TextSecondary = "#705F46",
            TextMuted = "#9A876A",
            Accent = "#B98235",
            AccentSecondary = "#D9A85C",
            Border = "#DEC9A4",
            Surface = "#FFFDF7",
            Shadow = "#40321C",
            Danger = "#C95243",
            Done = "#4B896B"
        },
        new()
        {
            Id = "violet",
            Name = "暮光紫",
            Description = "温柔的灵感时刻",
            BackgroundStart = "#F5EEFF",
            BackgroundEnd = "#E5D9FF",
            TextPrimary = "#2B1D45",
            TextSecondary = "#65557F",
            TextMuted = "#9385AA",
            Accent = "#7857D8",
            AccentSecondary = "#B07EE7",
            Border = "#D3C1EB",
            Surface = "#FCF8FF",
            Shadow = "#2E1E50",
            Danger = "#D64E61",
            Done = "#3C977B"
        }
    };

    public static WidgetSkin Get(string? id)
    {
        return All.FirstOrDefault(skin => string.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }
}
