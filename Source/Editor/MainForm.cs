// MainForm.cs — Main editor window.
//
// Architecture mirrors FlaxEngine's EditorWindow / WindowsModule pattern:
//   - C# owns the window lifetime and UI (menus, panels)
//   - C++ (JzRE.Runtime.dll) owns the GPU device and render loop
//   - The two sides communicate only through the flat P/Invoke API in NativeRuntime.cs
//
// Orbit camera: left-drag to rotate, scroll wheel to zoom.

using System;
using System.Windows.Forms;
using JzRE.Editor.Interop;

namespace JzRE.Editor;

public sealed class MainForm : Form
{
    private readonly RenderPanel _renderPanel = new();
    private System.Windows.Forms.Timer? _renderTimer;

    // Orbit camera state
    private float _distance = 5f;
    private float _pitch    = 0.3f;
    private float _yaw      = 0f;
    private Point _lastMouse;
    private bool  _dragging;

    public MainForm()
    {
        Text            = "JzRE-mix  |  Rendering Engine";
        ClientSize      = new System.Drawing.Size(1280, 720);
        MinimumSize     = new System.Drawing.Size(400, 300);

        BuildMenu();
        BuildRenderPanel();
    }

    // ── UI Construction ─────────────────────────────────────────────────────

    private void BuildMenu()
    {
        var strip    = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("File");

        var openItem = new ToolStripMenuItem("Open OBJ Model…", null, OnOpenFile)
        {
            ShortcutKeys = Keys.Control | Keys.O
        };
        fileMenu.DropDownItems.Add(openItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("Exit", null, (_, _) => Close()));

        var helpMenu = new ToolStripMenuItem("Help");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("Controls", null, ShowControls));

        strip.Items.Add(fileMenu);
        strip.Items.Add(helpMenu);
        Controls.Add(strip);
        MainMenuStrip = strip;
    }

    private void BuildRenderPanel()
    {
        _renderPanel.Dock        = DockStyle.Fill;
        _renderPanel.MouseDown  += (_, e) => { _dragging = true;  _lastMouse = e.Location; };
        _renderPanel.MouseUp    += (_, e) =>   _dragging = false;
        _renderPanel.MouseMove  += OnMouseMove;
        _renderPanel.MouseWheel += OnMouseWheel;
        _renderPanel.Resize     += (_, _) => OnRenderPanelResize();
        Controls.Add(_renderPanel);
        _renderPanel.BringToFront();
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var panel = _renderPanel;
        if (!NativeRuntime.Renderer_Create(panel.Handle, panel.Width, panel.Height))
        {
            MessageBox.Show(
                "Failed to initialize D3D11 renderer:\n\n" + NativeRuntime.Renderer_GetLastError(),
                "JzRE.Runtime Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 60 fps render loop driven by a WinForms timer (mirrors EditorModule.OnUpdate)
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += (_, _) => NativeRuntime.Renderer_Render();
        _renderTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _renderTimer?.Stop();
        NativeRuntime.Renderer_Destroy();
        base.OnFormClosed(e);
    }

    // ── Event Handlers ───────────────────────────────────────────────────────

    private void OnRenderPanelResize()
    {
        var p = _renderPanel;
        if (p.Width > 0 && p.Height > 0)
            NativeRuntime.Renderer_Resize(p.Width, p.Height);
    }

    private void OnOpenFile(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open 3D Model",
            Filter = "Wavefront OBJ (*.obj)|*.obj|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        if (!NativeRuntime.Renderer_LoadFile(dlg.FileName))
        {
            MessageBox.Show(
                "Failed to load model:\n\n" + NativeRuntime.Renderer_GetLastError(),
                "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Text = $"JzRE-mix  |  {System.IO.Path.GetFileName(dlg.FileName)}";

        // Reset camera to default view
        _distance = 5f; _pitch = 0.3f; _yaw = 0f;
        NativeRuntime.Renderer_SetViewAngle(_distance, _pitch, _yaw);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        float dx = e.X - _lastMouse.X;
        float dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;
        _yaw  += dx * 0.01f;
        _pitch = Math.Clamp(_pitch + dy * 0.01f, -1.5f, 1.5f);
        NativeRuntime.Renderer_SetViewAngle(_distance, _pitch, _yaw);
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        _distance = Math.Max(0.1f, _distance - e.Delta * 0.005f);
        NativeRuntime.Renderer_SetViewAngle(_distance, _pitch, _yaw);
    }

    private void ShowControls(object? sender, EventArgs e) =>
        MessageBox.Show(
            "Left mouse drag  — orbit camera\n" +
            "Scroll wheel     — zoom in / out\n" +
            "File > Open OBJ  — load a 3D model\n",
            "Controls", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
