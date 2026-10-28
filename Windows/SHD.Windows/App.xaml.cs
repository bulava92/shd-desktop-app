using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SHD.Windows;

public partial class App : Application
{
    private const string MutexName = @"Local\SHD.Desktop.Windows";
    private const string PipeName = "SHD.Desktop.Windows.Auth";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private MainWindow? _mainWindow;
    private readonly CancellationTokenSource _pipeCancellation = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            var command = e.Args.FirstOrDefault(IsDesktopAuthCallback) ?? "activate";
            await ForwardToPrimaryInstanceAsync(command);
            Shutdown();
            return;
        }

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
        _ = ListenForSecondInstanceAsync(_pipeCancellation.Token);

        var callback = e.Args.FirstOrDefault(IsDesktopAuthCallback);
        if (callback is not null)
        {
            await _mainWindow.Dispatcher.InvokeAsync(() => _mainWindow.HandleAuthCallback(callback), DispatcherPriority.Loaded);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCancellation.Cancel();
        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _instanceMutex?.Dispose();
        _pipeCancellation.Dispose();
        base.OnExit(e);
    }

    private async Task ListenForSecondInstanceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Utf8WithoutBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var message = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(message)) continue;

                await Dispatcher.InvokeAsync(() =>
                {
                    var windows = Application.Current.Windows.OfType<MainWindow>().ToArray();
                    if (IsDesktopAuthCallback(message))
                    {
                        var authWindow = windows.FirstOrDefault(window => window.CanHandleAuthCallback(message));
                        if (authWindow is not null)
                        {
                            if (authWindow.WindowState == WindowState.Minimized) authWindow.WindowState = WindowState.Normal;
                            authWindow.Activate();
                            authWindow.HandleAuthCallback(message);
                            return;
                        }
                    }

                    var fallbackWindow = windows.FirstOrDefault() ?? _mainWindow;
                    if (fallbackWindow is null) return;
                    if (fallbackWindow.WindowState == WindowState.Minimized) fallbackWindow.WindowState = WindowState.Normal;
                    fallbackWindow.Activate();
                    if (IsDesktopAuthCallback(message)) fallbackWindow.HandleAuthCallback(message);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Delay(150, cancellationToken);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task ForwardToPrimaryInstanceAsync(string command)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(500);
                await using var writer = new StreamWriter(client, Utf8WithoutBom, bufferSize: 1024, leaveOpen: true);
                await writer.WriteLineAsync(command);
                await writer.FlushAsync();
                return;
            }
            catch (IOException)
            {
                await Task.Delay(150);
            }
            catch (TimeoutException)
            {
                await Task.Delay(150);
            }
        }
    }

    private static bool IsDesktopAuthCallback(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals("shd-auth", StringComparison.OrdinalIgnoreCase);
}
