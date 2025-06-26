using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdeptiScanner_ZZZ
{
    class GameVisibilityHandler
    {
        static IntPtr game = IntPtr.Zero;
        public static bool enabled = true;
        public static bool captureGameProcess()
        {
            if (!enabled)
            {
                return false;
            }
            Process[] ans = Process.GetProcessesByName("ZenlessZoneZero");
            foreach(Process proc in ans)
            {
                if (proc.MainWindowTitle == "ZenlessZoneZero")
                {
                    game = proc.MainWindowHandle;
                    break;
                }
            }
            if (! ScannerForm.IsAdministrator())
            {
                game = IntPtr.Zero;
            }
            if (game == IntPtr.Zero)
            {
                return false;
            }
            return true;
        }

        public static void bringGameToFront()
        {
            if (game == IntPtr.Zero || !enabled)
            {
                return;
            }
            SetForegroundWindow(game.ToInt32());
            ShowWindow(game.ToInt32(), 9); //9 = SW_RESTORE
            System.Threading.Thread.Sleep(100);
        }

        public static bool getGameLocation(out System.Drawing.Rectangle gameLocation)
        {
            gameLocation = System.Drawing.Rectangle.Empty;
            if (game == IntPtr.Zero || !enabled)
            {
                return false;
            }

            RECT windowRect;
            RECT clientRect;

            // windowRect includes drop shadow and window header
            // clientRect is correct game size, but not location
            bool windowRectValid = GetWindowRect(game.ToInt32(), out windowRect);
            bool clientRectValid = GetClientRect(game.ToInt32(), out clientRect);
            if (!windowRectValid || !clientRectValid)
            {
                return false;
            }

            var gameWidth = clientRect.Right - clientRect.Left;
            var gameHeight = clientRect.Bottom - clientRect.Top;

            var windowWidth = windowRect.Right - windowRect.Left;
            var windowHeight = windowRect.Bottom - windowRect.Top;

            var extraWidth = windowWidth - gameWidth; // drop shadow on each side
            var extraHeight = windowHeight - gameHeight; // window header at the top, drop shadow at the bottom

            var dropShadowWidth = extraWidth / 2;
            var titleBarHeight = extraHeight - dropShadowWidth;

            gameLocation = new System.Drawing.Rectangle(windowRect.Left + dropShadowWidth, windowRect.Top + titleBarHeight, gameWidth, gameHeight);
            return true;
        }

        public static bool? IsGameFocused()
        {
            if (game == IntPtr.Zero ||!enabled)
            {
                return null;
            }

            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground == game)
            {
                return true;
            }
            return false;
        }

        public static void bringScannerToFront()
        {
            if (ScannerForm.INSTANCE.InvokeRequired)
            {
                ScannerForm.INSTANCE.Invoke(new Action(bringScannerToFront));
                return;
            }
            SetForegroundWindow(ScannerForm.INSTANCE.Handle.ToInt32());
        }

        [DllImport("user32.dll")]
        public static extern Int32 SetForegroundWindow(int hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(int hWnd, int nCmdShow);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(int hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetClientRect(int hWnd, out RECT lpRect);

    }
}
