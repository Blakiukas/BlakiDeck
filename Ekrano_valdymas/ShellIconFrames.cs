using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ekrano_valdymas;

internal static class ShellIconFrames
{
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] data,uint size,bool icon,uint version,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index,uint dpi);
    internal static void Attach(Window window)
    {
        IntPtr small=IntPtr.Zero,large=IntPtr.Zero;
        void Apply()
        {
            var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;
            using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico"))!.Stream;
            using var memory=new MemoryStream();stream.CopyTo(memory);var bytes=memory.ToArray();
            IntPtr Read(int size)
            {
                int count=BitConverter.ToUInt16(bytes,4),entry=6,best=int.MaxValue;
                for(int i=0;i<count;i++)
                {
                    int at=6+i*16,width=bytes[at]==0?256:bytes[at];int distance=Math.Abs(width-size);
                    if(distance<best){best=distance;entry=at;}
                }
                int length=BitConverter.ToInt32(bytes,entry+8),offset=BitConverter.ToInt32(bytes,entry+12);
                return CreateIconFromResourceEx(bytes.AsSpan(offset,length).ToArray(),(uint)length,true,0x30000,size,size,0);
            }
            uint dpi=GetDpiForWindow(hwnd);
            var nextSmall=Read(GetSystemMetricsForDpi(49,dpi));var nextLarge=Read(GetSystemMetricsForDpi(11,dpi));
            if(nextSmall!=IntPtr.Zero){SendMessage(hwnd,0x80,IntPtr.Zero,nextSmall);if(small!=IntPtr.Zero)DestroyIcon(small);small=nextSmall;}
            if(nextLarge!=IntPtr.Zero){SendMessage(hwnd,0x80,new IntPtr(1),nextLarge);if(large!=IntPtr.Zero)DestroyIcon(large);large=nextLarge;}
        }
        window.Loaded+=(_,_)=>Apply();
        window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(Apply));
        window.Closed+=(_,_)=>{if(small!=IntPtr.Zero)DestroyIcon(small);if(large!=IntPtr.Zero)DestroyIcon(large);};
    }
}
