using Qingdan.Common;

namespace Qingdan.Models;

public sealed class AppSettings : ObservableObject
{
    private bool _autoStart = true;
    private bool _showWidget = true;
    private bool _showCompletedInWidget;
    private string _widgetSkinId = "mist";
    private double _widgetBackgroundOpacity = 0.9;
    private double? _widgetLeft;
    private double? _widgetTop;
    private bool _firstRun = true;

    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    public bool ShowWidget
    {
        get => _showWidget;
        set => SetProperty(ref _showWidget, value);
    }

    public bool ShowCompletedInWidget
    {
        get => _showCompletedInWidget;
        set => SetProperty(ref _showCompletedInWidget, value);
    }

    public string WidgetSkinId
    {
        get => _widgetSkinId;
        set => SetProperty(ref _widgetSkinId, string.IsNullOrWhiteSpace(value) ? "mist" : value);
    }

    public double WidgetBackgroundOpacity
    {
        get => _widgetBackgroundOpacity;
        set => SetProperty(ref _widgetBackgroundOpacity, Math.Clamp(value, 0.35, 1.0));
    }

    public double? WidgetLeft
    {
        get => _widgetLeft;
        set => SetProperty(ref _widgetLeft, value);
    }

    public double? WidgetTop
    {
        get => _widgetTop;
        set => SetProperty(ref _widgetTop, value);
    }

    public bool FirstRun
    {
        get => _firstRun;
        set => SetProperty(ref _firstRun, value);
    }
}
