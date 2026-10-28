using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace SHD.Windows;

internal sealed class BrowserTab(WebView2 view, TabItem item, ProgressBar progress, TextBlock title, ShdEnvironment environment)
{
    public WebView2 View { get; } = view;
    public TabItem Item { get; } = item;
    public ProgressBar Progress { get; } = progress;
    public TextBlock Title { get; } = title;
    public ShdEnvironment Environment { get; set; } = environment;
}
