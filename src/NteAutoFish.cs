using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NteAutoFish
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] p, int cb);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight;
            public ushort biPlanes; public ushort biBitCount; public uint biCompression;
            public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO { public BITMAPINFOHEADER h; public uint c0, c1, c2; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT { public uint uMsg; public ushort l, h; }
        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION u; }

        public const uint INPUT_KEYBOARD = 1, INPUT_MOUSE = 0;
        public const uint KEYUP = 0x0002, SCANCODE = 0x0008;
        public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
        public const uint SRCCOPY = 0x00CC0020;

        public static void Key(ushort scan, bool up)
        {
            INPUT[] a = new INPUT[1];
            a[0].type = INPUT_KEYBOARD;
            a[0].u.ki.wScan = scan;
            a[0].u.ki.dwFlags = SCANCODE | (up ? KEYUP : 0);
            SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void Click(int sx, int sy)
        {
            SetCursorPos(sx, sy);
            Thread.Sleep(45);
            Mouse(LEFTDOWN); Thread.Sleep(55); Mouse(LEFTUP);
        }

        static void Mouse(uint f)
        {
            INPUT[] a = new INPUT[1];
            a[0].type = INPUT_MOUSE;
            a[0].u.mi.dwFlags = f;
            SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
        }

        public static ushort Scan(string key)
        {
            switch (key)
            {
                case "ESC": return 0x01;
                case "F": return 0x21;
                case "R": return 0x13;
                case "A": return 0x1E;
                case "D": return 0x20;
                case "Q": return 0x10;
                case "E": return 0x12;
                case "SPACE": return 0x39;
                default: return 0;
            }
        }
    }

    /// <summary>Reusable GDI screen grabber (one allocation per resolution change).</summary>
    sealed class Grabber : IDisposable
    {
        IntPtr _screen, _mem, _bmp, _old;
        int _w, _h;
        byte[] _buf;
        Native.BITMAPINFO _bi;

        public byte[] Buffer { get { return _buf; } }
        public int Width { get { return _w; } }
        public int Height { get { return _h; } }

        public void Ensure(int w, int h)
        {
            if (w == _w && h == _h && _buf != null) return;
            Release();
            _w = w; _h = h;
            _screen = Native.GetDC(IntPtr.Zero);
            _mem = Native.CreateCompatibleDC(_screen);
            _bmp = Native.CreateCompatibleBitmap(_screen, w, h);
            _old = Native.SelectObject(_mem, _bmp);
            _buf = new byte[w * h * 4];
            _bi = new Native.BITMAPINFO();
            _bi.h.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            _bi.h.biWidth = w;
            _bi.h.biHeight = -h;
            _bi.h.biPlanes = 1;
            _bi.h.biBitCount = 32;
            _bi.h.biCompression = 0;
        }

        public void Capture(int screenX, int screenY)
        {
            Native.BitBlt(_mem, 0, 0, _w, _h, _screen, screenX, screenY, Native.SRCCOPY);
            Native.GetDIBits(_mem, _bmp, 0, (uint)_h, _buf, ref _bi, 0);
        }

        void Release()
        {
            if (_mem != IntPtr.Zero)
            {
                if (_old != IntPtr.Zero) Native.SelectObject(_mem, _old);
                if (_bmp != IntPtr.Zero) Native.DeleteObject(_bmp);
                Native.DeleteDC(_mem);
                _mem = _bmp = _old = IntPtr.Zero;
            }
            if (_screen != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, _screen); _screen = IntPtr.Zero; }
            _buf = null;
        }

        public void Dispose() { Release(); }
    }

    struct BarInfo
    {
        public bool Seen;
        public int GreenMin, GreenMax, Tick, Center, GreenCount;
    }

    sealed class GameWindow
    {
        public IntPtr Handle;
        public int X, Y, W, H;

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);

        public static GameWindow Find()
        {
            GameWindow found = null;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                StringBuilder cls = new StringBuilder(256);
                GetClassNameW(h, cls, 256);
                if (cls.ToString() != "UnrealWindow") return true;
                Native.RECT r;
                Native.GetClientRect(h, out r);
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                if (w < 640 || hh < 360) return true;
                GameWindow g = new GameWindow();
                g.Handle = h;
                Native.POINT p = new Native.POINT();
                Native.ClientToScreen(h, ref p);
                g.X = p.X; g.Y = p.Y; g.W = w; g.H = hh;
                found = g;
                return false;
            }, IntPtr.Zero);
            return found;
        }
    }

    static class Vision
    {
        public static BarInfo ReadBar(byte[] buf, int w, int h)
        {
            BarInfo bi = new BarInfo();
            int y0 = (int)(h * 0.045), y1 = (int)(h * 0.090);
            int x0 = (int)(w * 0.290), x1 = (int)(w * 0.715);
            int t0 = (int)(w * 0.340), t1 = (int)(w * 0.680);

            int gmin = int.MaxValue, gmax = -1, gcount = 0;
            int[] col = new int[w];
            for (int y = y0; y < y1; y++)
            {
                int row = y * w * 4;
                for (int x = x0; x < x1; x++)
                {
                    int i = row + x * 4;
                    byte b = buf[i], g = buf[i + 1], r = buf[i + 2];
                    if (g > 140 && (g - r) > 35 && (g - b) > 10)
                    {
                        gcount++;
                        if (x < gmin) gmin = x;
                        if (x > gmax) gmax = x;
                    }
                    if (x >= t0 && x < t1 && r > 165 && g > 135 && b < 170 && (r - b) > 40) col[x]++;
                }
            }
            int tick = -1, best = 0;
            for (int x = t0; x < t1; x++) if (col[x] > best) { best = col[x]; tick = x; }

            bi.GreenCount = gcount;
            bi.GreenMin = gmin; bi.GreenMax = gmax;
            bi.Tick = tick;
            bi.Seen = (gcount > 60) && gmax > 0 && (gmax - gmin) < (int)(w * 0.17) && best >= 6;
            bi.Center = (gmin + gmax) / 2;
            return bi;
        }

        public static double CardScore(byte[] buf, int w, int h)
        {
            int x0 = (int)(w * 0.350), x1 = (int)(w * 0.650);
            int y0 = (int)(h * 0.278), y1 = (int)(h * 0.578);
            int bright = 0, total = 0;
            for (int y = y0; y < y1; y += 4)
            {
                int row = y * w * 4;
                for (int x = x0; x < x1; x += 4)
                {
                    int i = row + x * 4;
                    if ((buf[i] + buf[i + 1] + buf[i + 2]) / 3 > 150) bright++;
                    total++;
                }
            }
            return total == 0 ? 0 : (double)bright / total;
        }
    }

    static class Config
    {
        public static void Load(Bot bot, string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    string def =
                        "# NteAutoFish\r\n" +
                        "# StallSeconds: 多少秒没咬钩就停手\r\n" +
                        "# CardThresholdPct: 结算卡识别阈值，16 表示 16%\r\n" +
                        "StallSeconds=70\r\n" +
                        "CardThresholdPct=16\r\n";
                    File.WriteAllText(path, def, new UTF8Encoding(true));
                    return;
                }
                foreach (string raw in File.ReadAllLines(path))
                {
                    string s = raw.Trim();
                    if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";")) continue;
                    int eq = s.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = s.Substring(0, eq).Trim();
                    string v = s.Substring(eq + 1).Trim();
                    int n;
                    if (!int.TryParse(v, out n)) continue;
                    if (k.Equals("StallSeconds", StringComparison.OrdinalIgnoreCase)) bot.StallSeconds = n;
                    else if (k.Equals("CardThresholdPct", StringComparison.OrdinalIgnoreCase)) bot.CardThresholdPct = n;
                }
            }
            catch { }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] == "--diag") { RunDiag(); return; }

                bool created;
                using (Mutex mtx = new Mutex(true, "Global\\NteAutoFish", out created))
                {
                    if (!created)
                    {
                        MessageBox.Show("NTE AutoFish 已经在运行了（看屏幕右下角托盘）。", "NTE AutoFish");
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TrayApp());
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "boot-error.log"),
                        DateTime.Now + "  " + ex + "\r\n");
                }
                catch { }
                MessageBox.Show("启动失败：" + ex.Message + "\n\n详细信息见 boot-error.log", "NTE AutoFish");
            }
        }

        static void RunDiag()
        {
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag.log");
            GameWindow g = GameWindow.Find();
            if (g == null) { File.WriteAllText(log, "game window not found\r\n"); return; }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("window {0}x{1} at {2},{3}", g.W, g.H, g.X, g.Y));
            using (Grabber grab = new Grabber())
            {
                grab.Ensure(g.W, g.H);
                Stopwatch sw = Stopwatch.StartNew();
                int frames = 0;
                while (sw.Elapsed.TotalSeconds < 5)
                {
                    grab.Capture(g.X, g.Y);
                    BarInfo b = Vision.ReadBar(grab.Buffer, g.W, g.H);
                    double c = Vision.CardScore(grab.Buffer, g.W, g.H);
                    frames++;
                    if (frames % 20 == 0)
                        sb.AppendLine(string.Format("fight={0} tick={1} card={2:F3} fps={3:F1}",
                            b.Seen, b.Tick, c, frames / sw.Elapsed.TotalSeconds));
                }
                sb.AppendLine("frames=" + frames);
            }
            File.WriteAllText(log, sb.ToString());
        }
    }

    /// <summary>Tray icon + menu. No console window.</summary>
    sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon _icon;
        readonly MenuItem _toggle;
        readonly MenuItem _status;
        readonly System.Windows.Forms.Timer _hotkey;
        readonly Bot _bot;
        bool _prevF12;

        public TrayApp()
        {
            _bot = new Bot();
            _bot.Log += OnBotLog;
            _bot.Status += OnBotStatus;

            _status = new MenuItem("未开始");
            _status.Enabled = false;
            _toggle = new MenuItem("开始钓鱼", new EventHandler(OnToggle));
            MenuItem openLog = new MenuItem("打开日志", new EventHandler(delegate { OpenInNotepad("fish.log"); }));
            MenuItem openCfg = new MenuItem("打开 config.ini", new EventHandler(delegate { OpenInNotepad("config.ini"); }));
            MenuItem exit = new MenuItem("退出", new EventHandler(OnExit));

            ContextMenu menu = new ContextMenu(new MenuItem[] {
                _status, new MenuItem("-"), _toggle, new MenuItem("-"), openLog, openCfg, new MenuItem("-"), exit });

            _icon = new NotifyIcon();
            _icon.Icon = MakeIcon();
            _icon.Text = "NTE AutoFish - 未开始";
            _icon.ContextMenu = menu;
            _icon.Visible = true;
            _icon.DoubleClick += new EventHandler(OnToggle);

            _hotkey = new System.Windows.Forms.Timer();
            _hotkey.Interval = 80;
            _hotkey.Tick += new EventHandler(OnHotkeyTick);
            _hotkey.Start();

            Config.Load(_bot, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini"));
            ShowBalloon("NTE AutoFish", "已在托盘运行。进入钓鱼模式后右键选「开始钓鱼」，或双击图标。F12 也能开关。");
        }

        static Icon MakeIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush body = new SolidBrush(Color.FromArgb(45, 145, 225)))
                {
                    g.FillEllipse(body, 3, 9, 19, 15);
                    g.FillPolygon(body, new Point[] { new Point(21, 16), new Point(30, 8), new Point(30, 24) });
                }
                using (SolidBrush white = new SolidBrush(Color.White)) g.FillEllipse(white, 8, 13, 4, 4);
                using (Pen p = new Pen(Color.FromArgb(235, 235, 235), 1.4f)) g.DrawLine(p, 10, 24, 10, 30);
            }
            IntPtr h = bmp.GetHicon();
            Icon ic = (Icon)Icon.FromHandle(h).Clone();
            return ic;
        }

        void ShowBalloon(string title, string text)
        {
            try { _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info); } catch { }
        }

        void OpenInNotepad(string name)
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                if (!File.Exists(p)) File.WriteAllText(p, "");
                Process.Start("notepad.exe", "\"" + p + "\"");
            }
            catch { }
        }

        void OnToggle(object sender, EventArgs e)
        {
            if (_bot.Running)
            {
                _bot.Stop();
                _toggle.Text = "开始钓鱼";
                _status.Text = "已暂停";
                _icon.Text = "NTE AutoFish - 已暂停";
                ShowBalloon("已暂停", "所有按键已松开。");
                return;
            }
            _bot.Start();
            _toggle.Text = "暂停";
            _status.Text = "钓鱼中...";
            _icon.Text = "NTE AutoFish - 钓鱼中";
            ShowBalloon("开始钓鱼", "请勿再操作鼠标键盘。F12 可暂停。");
        }

        void OnExit(object sender, EventArgs e)
        {
            _bot.Stop();
            Thread.Sleep(150);
            _hotkey.Stop();
            _icon.Visible = false;
            _icon.Dispose();
            ExitThread();
        }

        void OnHotkeyTick(object sender, EventArgs e)
        {
            bool down = (Native.GetAsyncKeyState(0x7B) & 0x8000) != 0;
            if (down && !_prevF12) OnToggle(null, null);
            _prevF12 = down;
        }

        void OnBotLog(string line)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fish.log"), line + "\r\n"); }
            catch { }
        }

        void OnBotStatus(State s, int caught)
        {
            if (_icon == null) return;
            _status.Text = "已上鱼 " + caught + " 条";
            _icon.Text = "NTE AutoFish - 已上鱼 " + caught;
            if (s == State.Stopped)
            {
                _toggle.Text = "开始钓鱼";
                _icon.Text = "NTE AutoFish - 已停止";
                ShowBalloon("已停止", "共上鱼 " + caught + " 条。");
            }
        }
    }
}
