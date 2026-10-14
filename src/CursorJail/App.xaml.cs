using System.Threading;

namespace CursorJail;

public partial class App : Application
{
    private Mutex? _instance;
    private MainWindow? _panel;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--smoke-test")
        {
            try { SmokeTest.Run(e.Args.Skip(1).FirstOrDefault() ?? "artifacts/smoke"); Shutdown(0); }
            catch (Exception ex) { Console.Error.WriteLine(ex); File.WriteAllText("smoke-error.log", ex.ToString()); Shutdown(1); }
            return;
        }
        _instance = new Mutex(true, @"Local\CursorJail", out bool first);
        if (!first)
        {
            MessageBox.Show("CursorJail is already running. Open it from the system tray.", "CursorJail", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            bool released = _panel?.ReleaseForExit() ?? true;
            args.Handled = true;
            MessageBox.Show((released ? "CursorJail stopped after an error and released the cursor."
                : "CursorJail stopped after an error. Windows could not confirm cursor release; sign out of Windows if it remains confined.") + "\n\n" + args.Exception.Message,
                "CursorJail", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _panel?.ReleaseForExit();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _panel?.ReleaseForExit();
        _panel = new MainWindow();
        MainWindow = _panel;
        _panel.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _panel?.ReleaseForExit();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
