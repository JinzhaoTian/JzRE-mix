// MainWindow.axaml.cs — Main editor window.
//
// Architecture mirrors FlaxEngine's EditorWindow pattern:
//   - C# owns the window lifetime and UI (menus, controls)
//   - C++ (native library) owns the GPU device and render loop
//   - The two sides communicate only through the flat P/Invoke API

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using JzRE;

namespace JzRE.Editor;

public partial class MainWindow : Window
{
    private DispatcherTimer? _renderTimer;
    private bool _renderEngineInitialized;
    private bool _renderEngineInitScheduled;
    private readonly string? _initialModelPath;
    private DateTime _lastFrameTime;
    private bool _scriptingInitialized;

    // Orbit camera state
    private float _distance = 5f;
    private float _pitch    = 0.3f;
    private float _yaw      = 0f;
    private Avalonia.Point _lastPointer;
    private bool  _dragging;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? initialModelPath)
    {
        _initialModelPath = initialModelPath;
        InitializeComponent();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var host = RenderHost;

        ScheduleRenderEngineInitialization();

        // Pointer input for orbit camera
        host.PointerPressed  += OnPointerPressed;
        host.PointerReleased += OnPointerReleased;
        host.PointerMoved    += OnPointerMoved;
        host.PointerWheelChanged += OnPointerWheel;

        // Resize handling — use PropertyChanged to avoid Rx dependency
        host.PropertyChanged += (s, e2) =>
        {
            if (e2.Property == Visual.BoundsProperty)
            {
                host.UpdateNativeBounds();
                var bounds = host.Bounds;
                if (!_renderEngineInitialized)
                {
                    ScheduleRenderEngineInitialization();
                    return;
                }

                if (bounds.Width > 0 && bounds.Height > 0)
                    RenderEngine.Resize(0, 0, (int)bounds.Width, (int)bounds.Height);
            }
        };

        // 60 fps render loop with scripting engine tick
        _lastFrameTime = DateTime.UtcNow;
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            float dt = (float)(now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;

            // Clamp delta to avoid spiral-of-death after a long pause
            if (dt > 0.1f) dt = 0.1f;

            if (_scriptingInitialized)
                ScriptEngine.Update(dt);

            RenderEngine.Render();
        };
        _renderTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _renderTimer?.Stop();

        if (_scriptingInitialized)
        {
            ScriptEngine.Shutdown();
            _scriptingInitialized = false;
        }

        _renderEngineInitialized = false;
        RenderEngine.Destroy();
        base.OnClosed(e);
    }

    private void ScheduleRenderEngineInitialization()
    {
        if (_renderEngineInitialized || _renderEngineInitScheduled)
            return;

        _renderEngineInitScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _renderEngineInitScheduled = false;
            TryInitializeRenderEngine();
        }, DispatcherPriority.Loaded);
    }

    private void TryInitializeRenderEngine()
    {
        if (_renderEngineInitialized)
            return;

        var host = RenderHost;
        var bounds = host.Bounds;
        var hostHandle = host.TryGetNativeHandle();

        if (hostHandle == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
        {
            if (IsLoaded)
                Dispatcher.UIThread.Post(TryInitializeRenderEngine, DispatcherPriority.Background);
            return;
        }

        if (!RenderEngine.Initialize(hostHandle, (int)bounds.Width, (int)bounds.Height))
        {
            ShowErrorDialog("Failed to initialize render engine:\n\n" +
                            RenderEngine.GetLastError());
            return;
        }

        host.UpdateNativeBounds();
        _renderEngineInitialized = true;
        RenderEngine.SetViewAngle(_distance, _pitch, _yaw);

        // Initialize the scripting engine after the renderer is ready
        if (!_scriptingInitialized)
        {
            ScriptEngine.Initialize();
            _scriptingInitialized = true;
        }

        if (!string.IsNullOrWhiteSpace(_initialModelPath))
            LoadModel(_initialModelPath);
    }

    // ── Pointer Events (Orbit Camera) ──────────────────────────────────────

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            _dragging    = true;
            _lastPointer = e.GetPosition(this);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastPointer.X);
        float dy = (float)(pos.Y - _lastPointer.Y);
        _lastPointer = pos;
        _yaw  += dx * 0.01f;
        _pitch = Math.Clamp(_pitch + dy * 0.01f, -1.5f, 1.5f);
        RenderEngine.SetViewAngle(_distance, _pitch, _yaw);
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        _distance = Math.Max(0.1f, _distance - (float)e.Delta.Y * 0.5f);
        RenderEngine.SetViewAngle(_distance, _pitch, _yaw);
    }

    // ── Menu Handlers ──────────────────────────────────────────────────────

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (!_renderEngineInitialized)
        {
            ShowErrorDialog("Render engine is still initializing. Please try again in a moment.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open 3D Model",
            FileTypeFilter = new[] { new FilePickerFileType("Wavefront OBJ") { Patterns = new[] { "*.obj" } } },
            AllowMultiple = false
        });

        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;

        LoadModel(path);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void ShowControls(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Controls",
            Width = 320, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Left mouse drag  — orbit camera\n" +
                                            "Scroll wheel     — zoom in / out\n" +
                                            "File > Open OBJ  — load a 3D model",
                                    TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };
        (dialog.Content as StackPanel)!.Children[1].AddHandler(PointerPressedEvent, (_, _) => dialog.Close(),
            RoutingStrategies.Tunnel);
        await dialog.ShowDialog(this);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async void ShowErrorDialog(string message)
    {
        var dialog = new Window
        {
            Title = "Error",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };
        (dialog.Content as StackPanel)!.Children[1].AddHandler(PointerPressedEvent, (_, _) => dialog.Close(),
            RoutingStrategies.Tunnel);
        await dialog.ShowDialog(this);
    }

    private void LoadModel(string path)
    {
        if (!RenderEngine.LoadFile(path))
        {
            ShowErrorDialog("Failed to load model:\n\n" + RenderEngine.GetLastError());
            return;
        }

        Title = $"JzRE-mix  |  {System.IO.Path.GetFileName(path)}";
        _distance = Math.Max(0.1f, RenderEngine.GetSuggestedDistance());
        _pitch = 0.3f;
        _yaw = 0.6f;
        RenderEngine.SetViewAngle(_distance, _pitch, _yaw);
    }
}
