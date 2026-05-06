namespace JzRE;

public static partial class RenderEngine
{
    public static bool Initialize(IntPtr hwnd, int width, int height)
        => Create(hwnd, 0, 0, width, height);
}
