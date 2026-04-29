using System;
using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace JzRE.Editor;

public partial class App : Application
{
    private static string? s_initialModelPath;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(s_initialModelPath);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>True when --debug was passed on the command line.</summary>
    public static bool DebugMode { get; private set; }
    /// <summary>True when --debugwait was passed on the command line.</summary>
    public static bool WaitForDebugger { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Scan for debug flags before Avalonia consumes argv.
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--debug" || args[i] == "-debug")
                DebugMode = true;
            else if (args[i] == "--debugwait" || args[i] == "-debugwait")
                WaitForDebugger = true;
        }

        if (DebugMode && !Debugger.IsAttached)
            Debugger.Launch();

        if (WaitForDebugger)
        {
            while (!Debugger.IsAttached)
                Thread.Sleep(100);
        }

        // Ensure native code finds Shaders/ relative to the output directory
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        if (args.Length > 0 && File.Exists(args[0]))
            s_initialModelPath = Path.GetFullPath(args[0]);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
