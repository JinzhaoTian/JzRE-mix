using System.Runtime.CompilerServices;
using JzRE.Scripting;

namespace JzRE;

public static partial class ScriptEngine
{
    public static void Initialize()
    {
        unsafe
        {
            RegisterInteropCallbacks(
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&NativeInterop.FreeGCHandle,
                (IntPtr)(delegate* unmanaged[Cdecl]<int, IntPtr, void>)&NativeInterop.Log
            );
        }
        Init();
    }
}
