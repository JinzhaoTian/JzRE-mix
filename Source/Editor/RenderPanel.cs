// RenderPanel.cs — WinForms Panel that hosts the D3D11 swap chain.
//
// Critical: disable all GDI painting so D3D11 can own the HWND surface.
// Mirrors the pattern FlaxEngine uses for its native window panels.

using System.Windows.Forms;

namespace JzRE.Editor;

public sealed class RenderPanel : Panel
{
    public RenderPanel()
    {
        // Tell WinForms to stay out of our window — D3D11 owns it
        SetStyle(ControlStyles.Opaque              |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);
        DoubleBuffered = false;
    }

    // Suppress GDI background erase (prevents flicker / white flash)
    protected override void OnPaintBackground(PaintEventArgs e) { }

    // D3D11 Present handles presentation; WinForms OnPaint is unused
    protected override void OnPaint(PaintEventArgs e) { }

    protected override System.Drawing.Size DefaultSize => new(800, 600);
}
