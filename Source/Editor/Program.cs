// Program.cs — C# editor entry point.
// Mirrors FlaxEngine: the managed process starts here, initializes Windows Forms,
// then creates MainForm which in turn calls NativeRuntime.Renderer_Create()
// to initialize the C++ D3D11 renderer via P/Invoke.
//
// [STAThread] is required: OpenFileDialog and all OLE/COM calls (including the
// WinForms message loop itself) must run on a Single-Threaded Apartment thread.
// Top-level statements do NOT get [STAThread] automatically, so we use an
// explicit Main method here — same pattern as the standard WinForms template.

using System.Windows.Forms;

namespace JzRE.Editor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
