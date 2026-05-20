using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class ToggleDesktopIcons
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;
    private const int LVM_FIRST = 0x1000;
    private const int LVM_HITTEST = LVM_FIRST + 18;
    private const uint LVHT_ONITEMICON = 0x00000002;
    private const uint LVHT_ONITEMLABEL = 0x00000004;
    private const uint LVHT_ONITEMSTATEICON = 0x00000008;
    private const uint MEM_COMMIT = 0x00001000;
    private const uint MEM_RELEASE = 0x00008000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;

    private static readonly string MutexName = "Local\\DesktopDoubleClickIconToggler";
    private static LowLevelMouseProc mouseProc = MouseHookCallback;
    private static IntPtr mouseHook = IntPtr.Zero;
    private static Point lastClickPoint = Point.Empty;
    private static int lastClickTick;
    private static int lastToggleTick;
    private static bool suppressNextLeftUp;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main()
    {
        bool createdNew;
        using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
        {
            if (!createdNew)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (NotifyIcon notifyIcon = CreateNotifyIcon())
            {
                mouseHook = SetMouseHook(mouseProc);
                try
                {
                    Application.Run();
                }
                finally
                {
                    if (mouseHook != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(mouseHook);
                    }

                    notifyIcon.Visible = false;
                }
            }
        }
    }

    private static NotifyIcon CreateNotifyIcon()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += delegate { Application.Exit(); };
        menu.Items.Add(exitItem);

        NotifyIcon notifyIcon = new NotifyIcon();
        Icon associatedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        notifyIcon.Icon = associatedIcon ?? SystemIcons.Application;
        notifyIcon.Text = "桌面双击隐藏图标";
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.Visible = true;
        return notifyIcon;
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc)
    {
        using (Process currentProcess = Process.GetCurrentProcess())
        using (ProcessModule currentModule = currentProcess.MainModule)
        {
            IntPtr moduleHandle = GetModuleHandle(currentModule.ModuleName);
            return SetWindowsHookEx(WH_MOUSE_LL, proc, moduleHandle, 0);
        }
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            MSLLHOOKSTRUCT hookData = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

            if (message == WM_LBUTTONUP && suppressNextLeftUp)
            {
                suppressNextLeftUp = false;
                return new IntPtr(1);
            }

            if (message == WM_LBUTTONDOWN)
            {
                Point clickPoint = new Point(hookData.pt.x, hookData.pt.y);
                int now = Environment.TickCount;

                if (IsDoubleClick(clickPoint, now) && IsPointOnDesktopBlankArea(clickPoint))
                {
                    ToggleDesktopIconWindow();
                    lastToggleTick = now;
                    lastClickTick = 0;
                    suppressNextLeftUp = true;
                    return new IntPtr(1);
                }

                lastClickPoint = clickPoint;
                lastClickTick = now;
            }
        }

        return CallNextHookEx(mouseHook, nCode, wParam, lParam);
    }

    private static bool IsDoubleClick(Point point, int now)
    {
        if (lastClickTick == 0)
        {
            return false;
        }

        int doubleClickTime = GetDoubleClickTime();
        if (unchecked(now - lastClickTick) > doubleClickTime)
        {
            return false;
        }

        if (unchecked(now - lastToggleTick) <= doubleClickTime)
        {
            return false;
        }

        int maxX = Math.Max(4, GetSystemMetrics(SM_CXDOUBLECLK));
        int maxY = Math.Max(4, GetSystemMetrics(SM_CYDOUBLECLK));
        return Math.Abs(point.X - lastClickPoint.X) <= maxX &&
            Math.Abs(point.Y - lastClickPoint.Y) <= maxY;
    }

    private static bool IsPointOnDesktopBlankArea(Point point)
    {
        DesktopWindows desktop = FindDesktopWindows();
        if (desktop.ListView == IntPtr.Zero)
        {
            return false;
        }

        IntPtr target = WindowFromPoint(new POINT { x = point.X, y = point.Y });
        while (target != IntPtr.Zero)
        {
            if (target == desktop.ListView ||
                target == desktop.DefView ||
                target == desktop.Host ||
                target == desktop.Progman)
            {
                return !IsWindowVisible(desktop.ListView) || !IsDesktopIconAtPoint(desktop.ListView, point);
            }

            target = GetParent(target);
        }

        return false;
    }

    private static bool IsDesktopIconAtPoint(IntPtr listView, Point screenPoint)
    {
        POINT clientPoint = new POINT { x = screenPoint.X, y = screenPoint.Y };
        if (!ScreenToClient(listView, ref clientPoint))
        {
            return true;
        }

        uint processId;
        GetWindowThreadProcessId(listView, out processId);
        if (processId == 0)
        {
            return true;
        }

        IntPtr process = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE,
            false,
            processId);
        if (process == IntPtr.Zero)
        {
            return true;
        }

        IntPtr remoteBuffer = IntPtr.Zero;
        try
        {
            LVHITTESTINFO hitTest = new LVHITTESTINFO();
            hitTest.pt = clientPoint;
            hitTest.flags = 0;
            hitTest.iItem = -1;
            hitTest.iSubItem = 0;
            hitTest.iGroup = 0;

            int size = Marshal.SizeOf(typeof(LVHITTESTINFO));
            byte[] buffer = StructureToBytes(hitTest, size);

            remoteBuffer = VirtualAllocEx(process, IntPtr.Zero, new UIntPtr((uint)size), MEM_COMMIT, PAGE_READWRITE);
            if (remoteBuffer == IntPtr.Zero)
            {
                return true;
            }

            UIntPtr bytesWritten;
            if (!WriteProcessMemory(process, remoteBuffer, buffer, new UIntPtr((uint)size), out bytesWritten))
            {
                return true;
            }

            SendMessage(listView, LVM_HITTEST, IntPtr.Zero, remoteBuffer);

            byte[] result = new byte[size];
            UIntPtr bytesRead;
            if (!ReadProcessMemory(process, remoteBuffer, result, new UIntPtr((uint)size), out bytesRead))
            {
                return true;
            }

            LVHITTESTINFO tested = BytesToStructure<LVHITTESTINFO>(result);
            uint onItemFlags = LVHT_ONITEMICON | LVHT_ONITEMLABEL | LVHT_ONITEMSTATEICON;
            return tested.iItem >= 0 && (tested.flags & onItemFlags) != 0;
        }
        finally
        {
            if (remoteBuffer != IntPtr.Zero)
            {
                VirtualFreeEx(process, remoteBuffer, UIntPtr.Zero, MEM_RELEASE);
            }

            CloseHandle(process);
        }
    }

    private static byte[] StructureToBytes(object value, int size)
    {
        byte[] buffer = new byte[size];
        IntPtr localBuffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, localBuffer, false);
            Marshal.Copy(localBuffer, buffer, 0, size);
            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(localBuffer);
        }
    }

    private static T BytesToStructure<T>(byte[] buffer)
    {
        IntPtr localBuffer = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.Copy(buffer, 0, localBuffer, buffer.Length);
            return (T)Marshal.PtrToStructure(localBuffer, typeof(T));
        }
        finally
        {
            Marshal.FreeHGlobal(localBuffer);
        }
    }

    private static void ToggleDesktopIconWindow()
    {
        DesktopWindows desktop = FindDesktopWindows();
        if (desktop.ListView == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(desktop.ListView, IsWindowVisible(desktop.ListView) ? SW_HIDE : SW_SHOW);
    }

    private static DesktopWindows FindDesktopWindows()
    {
        DesktopWindows result = new DesktopWindows();
        result.Progman = FindWindow("Progman", null);

        IntPtr defView = FindWindowEx(result.Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            result.Host = result.Progman;
            result.DefView = defView;
            result.ListView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            return result;
        }

        EnumWindows(delegate(IntPtr topLevelWindow, IntPtr lParam)
        {
            IntPtr shellView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                result.Host = topLevelWindow;
                result.DefView = shellView;
                result.ListView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", null);
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(
        IntPtr hwndParent,
        IntPtr hwndChildAfter,
        string lpszClass,
        string lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelMouseProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        UIntPtr dwSize,
        uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        UIntPtr nSize,
        out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        UIntPtr nSize,
        out UIntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private struct DesktopWindows
    {
        public IntPtr Progman;
        public IntPtr Host;
        public IntPtr DefView;
        public IntPtr ListView;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
