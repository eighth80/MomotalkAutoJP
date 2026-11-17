using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MomotalkAutoJP.Core
{
    public enum CaptureMode
    {
        PrintWindow = 0, // 게임 창이 다른 창에 가려져도 캡처 가능 (기본)
        Screen = 1       // 화면에 보이는 그대로 캡처 (PrintWindow가 검게 나올 때)
    }

    /// <summary>블루아카이브 게임 창 찾기 / 캡처 / 클릭</summary>
    public sealed class GameWindow
    {
        // 일본판 PC 클라이언트에서 쓰일 수 있는 창 제목 후보
        private static readonly string[] KnownTitles =
        {
            "ブルーアーカイブ", "ブルーアーカイブ -Blue Archive-", "BlueArchive", "Blue Archive"
        };

        public IntPtr Handle { get; }
        public string Title { get; }

        private GameWindow(IntPtr handle, string title)
        {
            Handle = handle;
            Title = title;
        }

        public bool IsAlive => Win32.IsWindow(Handle);

        public (int Width, int Height) ClientSize
        {
            get
            {
                Win32.GetClientRect(Handle, out var rc);
                return (rc.Right - rc.Left, rc.Bottom - rc.Top);
            }
        }

        #region 창 찾기

        public record WindowEntry(IntPtr Handle, string Title, string ClassName, string ProcessName)
        {
            public override string ToString() => $"{Title}  ({ProcessName})";
        }

        /// <summary>제목이 있는 보이는 창 전부</summary>
        public static List<WindowEntry> ListWindows()
        {
            var list = new List<WindowEntry>();
            var procNames = new Dictionary<uint, string>();

            Win32.EnumWindowsProc cb = (h, _) =>
            {
                if (!Win32.IsWindowVisible(h)) return true;

                var title = new StringBuilder(512);
                Win32.GetWindowText(h, title, title.Capacity);
                if (title.Length == 0) return true;

                var cls = new StringBuilder(256);
                Win32.GetClassName(h, cls, cls.Capacity);

                Win32.GetWindowThreadProcessId(h, out uint pid);
                if (!procNames.TryGetValue(pid, out var pname))
                {
                    try { pname = Process.GetProcessById((int)pid).ProcessName; }
                    catch { pname = "?"; }
                    procNames[pid] = pname;
                }

                list.Add(new WindowEntry(h, title.ToString(), cls.ToString(), pname));
                return true;
            };

            Win32.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return list;
        }

        /// <summary>
        /// 게임 창 찾기. customTitle이 있으면 제목에 그 글자가 들어간 창을 최우선으로,
        /// 없으면 Unity 창 중에서 제목 후보 / BlueArchive 프로세스를 찾는다.
        /// </summary>
        public static GameWindow? Find(string? customTitle)
        {
            WindowEntry? best = null;
            int bestScore = -1;
            long bestArea = -1;

            foreach (var w in ListWindows())
            {
                int score = -1;
                if (!string.IsNullOrWhiteSpace(customTitle) &&
                    w.Title.Contains(customTitle.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    score = 3;
                }
                else if (w.ClassName == "UnityWndClass")
                {
                    if (KnownTitles.Contains(w.Title)) score = 2;
                    else if (w.ProcessName.Contains("BlueArchive", StringComparison.OrdinalIgnoreCase)) score = 1;
                }
                if (score < 0) continue;

                Win32.GetClientRect(w.Handle, out var rc);
                long area = (long)(rc.Right - rc.Left) * (rc.Bottom - rc.Top);
                if (area <= 0) continue;

                if (score > bestScore || (score == bestScore && area > bestArea))
                {
                    best = w;
                    bestScore = score;
                    bestArea = area;
                }
            }

            return best == null ? null : new GameWindow(best.Handle, best.Title);
        }

        #endregion

        #region 캡처

        /// <summary>게임 창 클라이언트 영역 캡처. 실패 시 null</summary>
        public Img? Capture(CaptureMode mode)
        {
            if (!IsAlive || Win32.IsIconic(Handle)) return null;

            var (cw, ch) = ClientSize;
            if (cw <= 0 || ch <= 0) return null;

            var origin = new Win32.POINT { X = 0, Y = 0 };
            Win32.ClientToScreen(Handle, ref origin);

            if (mode == CaptureMode.Screen)
                return CaptureFromScreen(origin.X, origin.Y, cw, ch);

            // PrintWindow는 창 전체(테두리 포함)를 그리므로 클라이언트 위치만큼 잘라냄
            Win32.GetWindowRect(Handle, out var wr);
            int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
            if (ww <= 0 || wh <= 0) return null;

            var full = CaptureWithPrintWindow(ww, wh);
            if (full == null) return null;

            var client = full.Crop(origin.X - wr.Left, origin.Y - wr.Top, cw, ch);
            return client.W > 0 && client.H > 0 ? client : null;
        }

        private Img? CaptureWithPrintWindow(int w, int h)
        {
            IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
            IntPtr memDc = Win32.CreateCompatibleDC(screenDc);
            IntPtr bmp = Win32.CreateCompatibleBitmap(screenDc, w, h);
            IntPtr old = Win32.SelectObject(memDc, bmp);
            try
            {
                if (!Win32.PrintWindow(Handle, memDc, Win32.PW_RENDERFULLCONTENT)) return null;
                Win32.SelectObject(memDc, old);
                return BitmapToImg(memDc, bmp, w, h);
            }
            finally
            {
                Win32.SelectObject(memDc, old);
                Win32.DeleteObject(bmp);
                Win32.DeleteDC(memDc);
                Win32.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static Img? CaptureFromScreen(int x, int y, int w, int h)
        {
            IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
            IntPtr memDc = Win32.CreateCompatibleDC(screenDc);
            IntPtr bmp = Win32.CreateCompatibleBitmap(screenDc, w, h);
            IntPtr old = Win32.SelectObject(memDc, bmp);
            try
            {
                if (!Win32.BitBlt(memDc, 0, 0, w, h, screenDc, x, y, Win32.SRCCOPY | Win32.CAPTUREBLT)) return null;
                Win32.SelectObject(memDc, old);
                return BitmapToImg(memDc, bmp, w, h);
            }
            finally
            {
                Win32.SelectObject(memDc, old);
                Win32.DeleteObject(bmp);
                Win32.DeleteDC(memDc);
                Win32.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static Img? BitmapToImg(IntPtr dc, IntPtr bmp, int w, int h)
        {
            var bi = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // 위에서 아래 방향
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32.BI_RGB
            };

            var bytes = new byte[w * h * 4];
            int lines = Win32.GetDIBits(dc, bmp, 0, (uint)h, bytes, ref bi, Win32.DIB_RGB_COLORS);
            if (lines == 0) return null;

            return new Img(w, h, bytes);
        }

        #endregion

        #region 입력

        /// <summary>게임 창을 맨 앞으로</summary>
        public void Focus()
        {
            if (Win32.GetForegroundWindow() == Handle) return;
            if (Win32.IsIconic(Handle)) Win32.ShowWindow(Handle, Win32.SW_RESTORE);

            // 다른 프로그램 창을 앞으로 가져오려면 Alt 입력으로 포커스 잠금을 풀어야 함
            SendKey(Win32.VK_MENU, 0, false);
            SendKey(Win32.VK_MENU, 0, true);
            Win32.SetForegroundWindow(Handle);
            Thread.Sleep(80);
        }

        /// <summary>클라이언트 좌표 (x, y) 클릭</summary>
        public void Click(int x, int y)
        {
            Focus();

            var pt = new Win32.POINT { X = x, Y = y };
            Win32.ClientToScreen(Handle, ref pt);

            int vx = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            int vy = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            int vw = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN));
            int vh = Math.Max(2, Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

            var move = new Win32.INPUT { type = Win32.INPUT_MOUSE };
            move.u.mi.dx = (int)Math.Round((pt.X - vx) * 65535.0 / (vw - 1));
            move.u.mi.dy = (int)Math.Round((pt.Y - vy) * 65535.0 / (vh - 1));
            move.u.mi.dwFlags = Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
            Send(move);
            Thread.Sleep(40);

            // Unity는 이동과 클릭이 같은 프레임이면 클릭을 놓칠 수 있어서 간격을 둠
            var down = new Win32.INPUT { type = Win32.INPUT_MOUSE };
            down.u.mi.dwFlags = Win32.MOUSEEVENTF_LEFTDOWN;
            Send(down);
            Thread.Sleep(60);

            var up = new Win32.INPUT { type = Win32.INPUT_MOUSE };
            up.u.mi.dwFlags = Win32.MOUSEEVENTF_LEFTUP;
            Send(up);
        }

        /// <summary>키 입력 (가상 키 코드). 게임이 스캔 코드를 읽는 경우가 많아서 스캔 코드로 보냄</summary>
        public void PressKey(ushort vk)
        {
            Focus();
            ushort scan = (ushort)Win32.MapVirtualKey(vk, 0);
            SendKey(0, scan, false);
            Thread.Sleep(60);
            SendKey(0, scan, true);
        }

        private static void SendKey(ushort vk, ushort scan, bool up)
        {
            var input = new Win32.INPUT { type = Win32.INPUT_KEYBOARD };
            input.u.ki.wVk = vk;
            input.u.ki.wScan = scan;
            input.u.ki.dwFlags = (scan != 0 ? Win32.KEYEVENTF_SCANCODE : 0) | (up ? Win32.KEYEVENTF_KEYUP : 0);
            Send(input);
        }

        private static void Send(Win32.INPUT input)
        {
            Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
        }

        #endregion
    }
}
