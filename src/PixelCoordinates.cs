using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexQuotaMonitor {
    // Override only the calling thread during synchronous native operations.
    static class PixelCoordinates {
        sealed class Context:IDisposable {IntPtr previous;public Context(IntPtr requested){try{previous=SetThreadDpiAwarenessContext(requested);}catch(EntryPointNotFoundException){}}public void Dispose(){if(previous!=IntPtr.Zero){SetThreadDpiAwarenessContext(previous);previous=IntPtr.Zero;}}}
        public static IDisposable Physical(){return new Context(new IntPtr(-4));}
        public static IDisposable Window(IntPtr handle){try{return new Context(GetWindowDpiAwarenessContext(handle));}catch(EntryPointNotFoundException){return Physical();}}
        public static Point Cursor {get{Position point;GetPhysicalCursorPos(out point);return new Point(point.X,point.Y);}set{SetPhysicalCursorPos(value.X,value.Y);}}
        [StructLayout(LayoutKind.Sequential)] struct Position {public int X,Y;}
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr handle);
        [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Position point);
        [DllImport("user32.dll")] static extern bool SetPhysicalCursorPos(int x,int y);
    }
}
