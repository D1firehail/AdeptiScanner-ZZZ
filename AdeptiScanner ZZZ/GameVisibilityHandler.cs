﻿using System;
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

            RECT rct;

            bool ans = GetWindowRect(game.ToInt32(), out rct);
            if (ans)
            {
                gameLocation = new System.Drawing.Rectangle(rct.Left, rct.Top, rct.Right - rct.Left, rct.Bottom - rct.Top);
            }
            return ans;
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

    }
}
