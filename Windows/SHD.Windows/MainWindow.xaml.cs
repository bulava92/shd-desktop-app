using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrawingColor = System.Drawing.Color;

namespace SHD.Windows;

public partial class MainWindow : Window
{
    private const string WebViewProfileFolder = "WebView2";
    private static readonly Uri WebViewRuntimePage = new("https://developer.microsoft.com/microsoft-edge/webview2/");
    private static readonly Lazy<Task<CoreWebView2Environment>> SharedWebViewEnvironment = new(CreateWebViewEnvironmentAsync);
    private readonly List<BrowserTab> _tabs = [];
    private readonly ShdEnvironment _initialEnvironment;
    private CoreWebView2Environment? _webViewEnvironment;
    private ShdEnvironment _selectedEnvironment;
    private PendingDesktopAuth? _authAttempt;
    private MemoryStream? _authPostData;
    private WebView2? _authPostTarget;
    private ulong? _authNavigationId;
    private bool _suppressEnvironmentChange;
    private bool _initialized;
    private bool _closing;
    private bool _isFullScreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;
    private Rect _previousBounds;

    public MainWindow(ShdEnvironment? initialEnvironment = null)
    {
        InitializeComponent();
        _initialEnvironment = initialEnvironment ?? SettingsStore.LoadEnvironment();
        _selectedEnvironment = _initialEnvironment;
        EnvironmentPicker.IsEnabled = false;
        Tabs.IsEnabled = false;
    }

    private BrowserTab? ActiveTab => Tabs.SelectedItem is TabItem item
        ? _tabs.FirstOrDefault(tab => ReferenceEquals(tab.Item, item))
        : null;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _webViewEnvironment = await SharedWebViewEnvironment.Value;
            SetEnvironmentPicker(_initialEnvironment);
            EnvironmentPicker.IsEnabled = true;
            Tabs.IsEnabled = true;
            await AddTabAsync(_initialEnvironment);
            StatusText.Text = "Готово";
        }
        catch (Exception)
        {
            var result = MessageBox.Show(
                this,
                "Не удалось запустить встроенный браузер SHD. Установите Microsoft Edge WebView2 Runtime и запустите приложение ещё раз.\n\nОткрыть страницу загрузки Runtime?",
                "SHD — встроенный браузер",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) OpenExternalUri(WebViewRuntimePage);
            Close();
        }
    }

    private static async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SHDDesktop",
            WebViewProfileFolder);
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
    }

    private async Task<BrowserTab> AddTabAsync(ShdEnvironment environment, Uri? initialUri = null)
    {
        if (_webViewEnvironment is null)
            throw new InvalidOperationException("WebView2 environment is not ready.");

        var view = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = DrawingColor.FromArgb(18, 18, 18)
        };
        var progress = new ProgressBar
        {
            Height = 2,
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromRgb(84, 156, 255)),
            Background = Brushes.Transparent
        };
        var content = new Grid { Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)) };
        content.Children.Add(view);
        Panel.SetZIndex(progress, 1);
        content.Children.Add(progress);

        var item = new TabItem
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            Foreground = Brushes.White,
            Content = content
        };
        var headerTitle = new TextBlock
        {
            Text = "SHD",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 210,
            Margin = new Thickness(2, 0, 7, 0)
        };
        var closeButton = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(3, 0, 0, 0),
            FontSize = 15,
            ToolTip = "Закрыть вкладку",
            VerticalAlignment = VerticalAlignment.Center
        };
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(2, 0, 2, 0) };
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);
        header.Children.Add(headerTitle);
        item.Header = header;

        var tab = new BrowserTab(view, item, progress, headerTitle, environment);
        closeButton.Click += (_, _) => CloseTab(tab);
        _tabs.Add(tab);
        Tabs.Items.Add(item);
        Tabs.SelectedItem = item;

        await view.EnsureCoreWebView2Async(_webViewEnvironment);
        await ConfigureBrowserAsync(tab);
        view.CoreWebView2.Navigate((initialUri ?? new Uri(environment.BaseUrl())).AbsoluteUri);
        UpdateWindowChrome();
        return tab;
    }

    private async Task ConfigureBrowserAsync(BrowserTab tab)
    {
        var core = tab.View.CoreWebView2;
        core.DefaultBackgroundColor = DrawingColor.FromArgb(18, 18, 18);
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsPasswordAutosaveEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = true;
        core.Settings.AreDefaultScriptDialogsEnabled = false;

        core.NavigationStarting += Browser_NavigationStarting;
        core.NavigationCompleted += Browser_NavigationCompleted;
        core.DocumentTitleChanged += Browser_DocumentTitleChanged;
        core.WebMessageReceived += Browser_WebMessageReceived;
        core.NewWindowRequested += Browser_NewWindowRequested;
        core.PermissionRequested += Browser_PermissionRequested;
        core.DownloadStarting += Browser_DownloadStarting;
        core.ScriptDialogOpening += Browser_ScriptDialogOpening;

        await core.AddScriptToExecuteOnDocumentCreatedAsync("""
            (() => {
                const darkStyle = document.createElement('style');
                darkStyle.id = '__shd_desktop_dark__';
                darkStyle.textContent = 'html, body { background-color: #121212 !important; color: #e0e0e0 !important; }';
                const parent = document.head || document.documentElement;
                if (parent) parent.appendChild(darkStyle);
                window.print = function () {
                    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('shd-print');
                };
            })();
            """);

        await core.AddScriptToExecuteOnDocumentCreatedAsync("""
            (() => {
                if (window.top !== window) return;
                document.addEventListener('click', event => {
                    let path = window.location.pathname;
                    while (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1);
                    if (path !== '/login' && path !== '/login/two-factor') return;
                    const target = event.target instanceof Element
                        ? event.target
                        : (event.target && event.target.parentElement);
                    if (!target || !target.closest('#btnPasskeyLogin, #twoFactorPasskey')) return;

                    event.preventDefault();
                    event.stopPropagation();
                    event.stopImmediatePropagation();
                    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('shd-passkey');
                }, true);
            })();
            """);
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (sender is not CoreWebView2 core) return;
        var tab = _tabs.FirstOrDefault(candidate => ReferenceEquals(candidate.View.CoreWebView2, core));
        if (tab is not null) tab.Progress.Visibility = Visibility.Visible;

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (tab is not null && ReferenceEquals(tab.View, _authPostTarget) &&
            uri.AbsolutePath.Equals("/desktop-auth/complete", StringComparison.Ordinal))
            _authNavigationId = e.NavigationId;
        if (uri.Scheme is not ("https" or "http"))
        {
            e.Cancel = true;
            if (e.IsUserInitiated) OpenExternalUri(uri);
            return;
        }

        if (tab is not null)
        {
            if (ShdEnvironmentExtensions.IsDevelopmentHost(uri.Host)) tab.Environment = ShdEnvironment.Dev;
            else if (ShdEnvironmentExtensions.IsProductionHost(uri.Host)) tab.Environment = ShdEnvironment.Production;
            if (ReferenceEquals(tab, ActiveTab))
            {
                _selectedEnvironment = tab.Environment;
                SetEnvironmentPicker(tab.Environment);
                SettingsStore.SaveEnvironment(tab.Environment);
                StatusEnvironment.Text = tab.Environment.DisplayName();
            }
        }

        if (e.IsUserInitiated && !e.IsRedirected && !ShdEnvironmentExtensions.IsInternalHost(uri.Host))
        {
            e.Cancel = true;
            OpenExternalUri(uri);
        }
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 core) return;
        var tab = _tabs.FirstOrDefault(candidate => ReferenceEquals(candidate.View.CoreWebView2, core));
        if (tab is not null) tab.Progress.Visibility = Visibility.Collapsed;
        UpdateWindowChrome();

        if (tab is not null && ReferenceEquals(_authPostTarget, tab.View) && _authNavigationId == e.NavigationId)
        {
            _authPostData?.Dispose();
            _authPostData = null;
            _authPostTarget = null;
            _authNavigationId = null;
            StatusText.Text = e.IsSuccess ? "Вход завершён" : "Не удалось завершить вход";
        }
    }

    private void Browser_DocumentTitleChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2 core) return;
        var tab = _tabs.FirstOrDefault(candidate => ReferenceEquals(candidate.View.CoreWebView2, core));
        if (tab is null) return;
        var title = CleanTitle(core.DocumentTitle);
        tab.Title.Text = title;
        if (ReferenceEquals(tab, ActiveTab))
        {
            PageTitle.Text = title;
            Title = $"{title} — SHD";
        }
    }

    private async void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is not WebView2 view || !Uri.TryCreate(e.Source, UriKind.Absolute, out var source) ||
            source.Scheme != Uri.UriSchemeHttps || !ShdEnvironmentExtensions.IsInternalHost(source.Host))
            return;

        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (InvalidOperationException) { return; }

        if (message == "shd-print")
        {
            view.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            return;
        }

        if (message != "shd-passkey") return;
        if (!(ShdEnvironmentExtensions.IsDevelopmentHost(source.Host) || ShdEnvironmentExtensions.IsProductionHost(source.Host))) return;
        var tab = _tabs.FirstOrDefault(candidate => ReferenceEquals(candidate.View, view));
        if (tab is null) return;
        var environment = ShdEnvironmentExtensions.IsDevelopmentHost(source.Host)
            ? ShdEnvironment.Dev
            : ShdEnvironment.Production;
        await BeginPasskeyLoginAsync(tab, environment);
    }

    private async void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is "http" or "https" && ShdEnvironmentExtensions.IsInternalHost(uri.Host))
        {
            var environment = ShdEnvironmentExtensions.IsDevelopmentHost(uri.Host)
                ? ShdEnvironment.Dev
                : ShdEnvironmentExtensions.IsProductionHost(uri.Host)
                    ? ShdEnvironment.Production
                    : _selectedEnvironment;
            await AddTabAsync(environment, uri);
        }
        else
        {
            OpenExternalUri(uri);
        }
    }

    private void Browser_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind is not (CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)) return;
        var trusted = Uri.TryCreate(e.Uri, UriKind.Absolute, out var origin) &&
                      origin.Scheme == Uri.UriSchemeHttps && ShdEnvironmentExtensions.IsInternalHost(origin.Host);
        if (trusted)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
        else
        {
            var label = e.PermissionKind == CoreWebView2PermissionKind.Camera ? "камеру" : "микрофон";
            var allow = MessageBox.Show(
                this,
                $"Разрешить сайту {e.Uri} доступ к устройству ({label})?",
                "Разрешение SHD",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
            e.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
        }
        e.Handled = true;
    }

    private void Browser_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var downloadsFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        downloadsFolder = Path.Combine(downloadsFolder, "Downloads");
        Directory.CreateDirectory(downloadsFolder);
        var suggestedName = Path.GetFileName(e.ResultFilePath);
        if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = "download";
        e.ResultFilePath = GetUniquePath(Path.Combine(downloadsFolder, suggestedName));
        e.Handled = true;
        StatusText.Text = $"Загрузка: {suggestedName}";
        e.DownloadOperation.StateChanged += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                    StatusText.Text = $"Загружено: {Path.GetFileName(e.ResultFilePath)}";
                else if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                    StatusText.Text = "Загрузка прервана";
            });
        };
    }

    private void Browser_ScriptDialogOpening(object? sender, CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        switch (e.Kind)
        {
            case CoreWebView2ScriptDialogKind.Alert:
                MessageBox.Show(this, e.Message, "SHD", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Accept();
                break;
            case CoreWebView2ScriptDialogKind.Confirm:
            case CoreWebView2ScriptDialogKind.BeforeUnload:
                if (MessageBox.Show(this, e.Message, "SHD", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    e.Accept();
                break;
            case CoreWebView2ScriptDialogKind.Prompt:
                var answer = ShowPrompt(e.Message, e.DefaultText);
                if (answer is not null)
                {
                    e.ResultText = answer;
                    e.Accept();
                }
                break;
        }
    }

    private async Task BeginPasskeyLoginAsync(BrowserTab tab, ShdEnvironment environment)
    {
        if (_authAttempt is not null)
        {
            MessageBox.Show(this, "Вход уже выполняется. Завершите его в браузере или закройте окно входа.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (environment != ShdEnvironment.Dev)
        {
            MessageBox.Show(this, "Вход по passkey в приложении пока подключён только для DEV.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var attempt = DesktopAuthFlow.Create(environment, tab);
            _authAttempt = attempt;
            Process.Start(new ProcessStartInfo(attempt.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
            StatusText.Text = "Продолжите вход в системном браузере";
        }
        catch (Exception)
        {
            _authAttempt = null;
            MessageBox.Show(this, "Не удалось открыть системный браузер для входа. Попробуйте ещё раз.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await Task.CompletedTask;
    }

    public bool CanHandleAuthCallback(string callbackValue)
    {
        if (_authAttempt is null) return false;
        if (!DesktopAuthFlow.TryParseCallback(callbackValue, out var callback, out _)) return false;
        return callback is not null && callback.State == _authAttempt.State;
    }

    public async void HandleAuthCallback(string callbackValue)
    {
        if (!DesktopAuthFlow.TryParseCallback(callbackValue, out var callback, out var serverError))
        {
            MessageBox.Show(this, "Сервер вернул некорректный ответ входа.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var attempt = _authAttempt;
        if (attempt is null)
        {
            MessageBox.Show(this, "Этот ответ входа уже нельзя использовать. Нажмите кнопку passkey на странице и начните заново.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (callback is null || callback.State != attempt.State)
        {
            MessageBox.Show(this, "Не удалось подтвердить ответ сервера. Начните вход заново.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (serverError)
        {
            _authAttempt = null;
            MessageBox.Show(this, "Сервер не завершил вход. Попробуйте ещё раз.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_tabs.Contains(attempt.TargetTab) || attempt.TargetTab.View.CoreWebView2 is null)
        {
            _authAttempt = null;
            MessageBox.Show(this, "Вкладка, из которой начался вход, уже закрыта. Начните вход заново.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _authAttempt = null;
            var formBody = $"code={DesktopAuthFlow.FormEncode(callback.Code)}&code_verifier={DesktopAuthFlow.FormEncode(attempt.Verifier)}";
            _authPostData?.Dispose();
            _authPostData = new MemoryStream(Encoding.UTF8.GetBytes(formBody));
            _authPostTarget = attempt.TargetTab.View;
            _authNavigationId = null;
            var request = _webViewEnvironment!.CreateWebResourceRequest(
                DesktopAuthFlow.CompleteUri(attempt.Environment).AbsoluteUri,
                "POST",
                _authPostData,
                "Content-Type: application/x-www-form-urlencoded; charset=utf-8\r\nCache-Control: no-store\r\n");
            attempt.TargetTab.Environment = attempt.Environment;
            Tabs.SelectedItem = attempt.TargetTab.Item;
            SetEnvironmentPicker(attempt.Environment);
            SettingsStore.SaveEnvironment(attempt.Environment);
            attempt.TargetTab.View.CoreWebView2.NavigateWithWebResourceRequest(request);
            StatusText.Text = "Завершаем вход…";
        }
        catch (Exception)
        {
            _authPostData?.Dispose();
            _authPostData = null;
            _authPostTarget = null;
            _authNavigationId = null;
            MessageBox.Show(this, "Не удалось завершить вход в выбранном контуре. Начните вход заново.", "Вход в SHD", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await Task.CompletedTask;
    }

    private void SetEnvironment(ShdEnvironment environment)
    {
        _selectedEnvironment = environment;
        SettingsStore.SaveEnvironment(environment);
        SetEnvironmentPicker(environment);
        StatusEnvironment.Text = environment.DisplayName();
        var tab = ActiveTab;
        if (tab is null || tab.View.CoreWebView2 is null) return;
        tab.Environment = environment;
        tab.View.CoreWebView2.Navigate(environment.BaseUrl());
    }

    private void SetEnvironmentPicker(ShdEnvironment environment)
    {
        _suppressEnvironmentChange = true;
        EnvironmentPicker.SelectedValue = environment.ToString();
        _suppressEnvironmentChange = false;
    }

    private async void NewTab_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewEnvironment is null) return;
        await AddTabAsync(_selectedEnvironment);
    }

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        var window = new MainWindow(_selectedEnvironment);
        window.Show();
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { } tab) CloseTab(tab);
    }

    private void CloseTab(BrowserTab tab)
    {
        if (_authAttempt?.TargetTab == tab) _authAttempt = null;
        if (ReferenceEquals(_authPostTarget, tab.View))
        {
            _authPostData?.Dispose();
            _authPostData = null;
            _authPostTarget = null;
            _authNavigationId = null;
        }
        tab.View.Dispose();
        _tabs.Remove(tab);
        Tabs.Items.Remove(tab.Item);
        if (_tabs.Count == 0 && !_closing) _ = AddTabAsync(_selectedEnvironment);
        UpdateWindowChrome();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tab = ActiveTab;
        if (tab is null) return;
        _selectedEnvironment = tab.Environment;
        SetEnvironmentPicker(tab.Environment);
        StatusEnvironment.Text = tab.Environment.DisplayName();
        PageTitle.Text = tab.Title.Text;
        Title = $"{CleanTitle(tab.View.CoreWebView2?.DocumentTitle ?? "SHD")} — SHD";
        UpdateNavigationButtons();
    }

    private void EnvironmentPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEnvironmentChange || !Enum.TryParse<ShdEnvironment>(EnvironmentPicker.SelectedValue as string, out var environment)) return;
        if (environment == _selectedEnvironment && ActiveTab?.Environment == environment) return;
        SetEnvironment(environment);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.T: NewTab_Click(sender, e); e.Handled = true; break;
                case Key.N: NewWindow_Click(sender, e); e.Handled = true; break;
                case Key.W: CloseTab_Click(sender, e); e.Handled = true; break;
                case Key.R: Reload_Click(sender, e); e.Handled = true; break;
                case Key.P: Print_Click(sender, e); e.Handled = true; break;
                case Key.D0: ZoomReset_Click(sender, e); e.Handled = true; break;
                case Key.Add:
                case Key.OemPlus: ZoomIn_Click(sender, e); e.Handled = true; break;
                case Key.Subtract:
                case Key.OemMinus: ZoomOut_Click(sender, e); e.Handled = true; break;
            }
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.R)
        {
            HardReload_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            DevTools_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            FullScreen_Click(sender, e);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Alt && e.Key == Key.Left)
        {
            Back_Click(sender, e);
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Alt && e.Key == Key.Right)
        {
            Forward_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab?.View.CoreWebView2?.CanGoBack == true) ActiveTab.View.CoreWebView2.GoBack();
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab?.View.CoreWebView2?.CanGoForward == true) ActiveTab.View.CoreWebView2.GoForward();
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => ActiveTab?.View.CoreWebView2?.Reload();

    private async void HardReload_Click(object sender, RoutedEventArgs e)
    {
        var core = ActiveTab?.View.CoreWebView2;
        if (core is null) return;
        try { await core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}"); }
        catch (InvalidOperationException) { core.Reload(); }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { } tab) tab.View.ZoomFactor = Math.Min(tab.View.ZoomFactor + 0.1, 3.0);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { } tab) tab.View.ZoomFactor = Math.Max(tab.View.ZoomFactor - 0.1, 0.4);
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { } tab) tab.View.ZoomFactor = 1.0;
    }

    private void Print_Click(object sender, RoutedEventArgs e) => ActiveTab?.View.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);

    private void DevTools_Click(object sender, RoutedEventArgs e) => ActiveTab?.View.CoreWebView2?.OpenDevToolsWindow();

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_isFullScreen)
        {
            _previousBounds = RestoreBounds;
            _previousWindowState = WindowState;
            _previousWindowStyle = WindowStyle;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _isFullScreen = true;
            return;
        }

        WindowStyle = _previousWindowStyle;
        WindowState = WindowState.Normal;
        Left = _previousBounds.Left;
        Top = _previousBounds.Top;
        Width = _previousBounds.Width;
        Height = _previousBounds.Height;
        WindowState = _previousWindowState;
        _isFullScreen = false;
    }

    private void Downloads_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(path);
        OpenExternalUri(new Uri(path));
    }

    private void SelectProduction_Click(object sender, RoutedEventArgs e) => SetEnvironment(ShdEnvironment.Production);

    private void SelectDev_Click(object sender, RoutedEventArgs e) => SetEnvironment(ShdEnvironment.Dev);

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Undo_Click(object sender, RoutedEventArgs e) => ExecuteEditCommand("undo");
    private void Redo_Click(object sender, RoutedEventArgs e) => ExecuteEditCommand("redo");
    private void Cut_Click(object sender, RoutedEventArgs e) => ExecuteEditCommand("cut");
    private void Copy_Click(object sender, RoutedEventArgs e) => ExecuteEditCommand("copy");
    private void Paste_Click(object sender, RoutedEventArgs e) => ExecuteEditCommand("paste");
    private void SelectAll_Click(object sender, RoutedEventArgs e) => ExecuteEditCommand("selectAll");

    private void ExecuteEditCommand(string command)
    {
        var escaped = command.Replace("'", "\\'", StringComparison.Ordinal);
        _ = ActiveTab?.View.CoreWebView2?.ExecuteScriptAsync($"document.execCommand('{escaped}')");
    }

    private void UpdateWindowChrome()
    {
        if (ActiveTab is { } tab)
        {
            PageTitle.Text = tab.Title.Text;
            StatusEnvironment.Text = tab.Environment.DisplayName();
        }
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = ActiveTab?.View.CoreWebView2?.CanGoBack == true;
        ForwardButton.IsEnabled = ActiveTab?.View.CoreWebView2?.CanGoForward == true;
    }

    private static string CleanTitle(string? raw)
    {
        var title = raw ?? string.Empty;
        foreach (var suffix in new[] { " — SHD Project", " - SHD Project", " | SHD" })
        {
            var index = title.IndexOf(suffix, StringComparison.Ordinal);
            if (index >= 0) title = title[..index];
        }
        return string.IsNullOrWhiteSpace(title) ? "SHD" : title.Trim();
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var counter = 1; ; counter++)
        {
            var candidate = Path.Combine(directory, $"{name} ({counter}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private string? ShowPrompt(string message, string defaultValue)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "SHD",
            Width = 430,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };
        var input = new TextBox { Text = defaultValue, MinWidth = 380, Margin = new Thickness(0, 9, 0, 14), Padding = new Thickness(6) };
        var accept = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
        panel.Children.Add(input);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        string? result = null;
        accept.Click += (_, _) => { result = input.Text; dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.ShowDialog();
        return result;
    }

    private void OpenExternalUri(Uri uri)
    {
        try
        {
            if (uri.IsFile)
            {
                Process.Start(new ProcessStartInfo(uri.LocalPath) { UseShellExecute = true });
                return;
            }
            if (uri.Scheme is "http" or "https" or "mailto")
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            StatusText.Text = "Не удалось открыть ссылку";
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _authAttempt = null;
        _authPostData?.Dispose();
        _authPostData = null;
        foreach (var tab in _tabs.ToArray()) tab.View.Dispose();
        _tabs.Clear();
    }
}
