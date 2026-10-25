// 1.11.228 (author, 24.10): the MelonLoader console must never freeze the game. A click inside the console window puts Windows
// into "Select" (QuickEdit) mode; from then on every line written to the console blocks the writer - the whole game - until Esc
// or Enter is pressed in the console. The author saw this twice on 24.10 as "the game hangs" (CPU 0 %, window title
// "Select MelonLoader ..."). QuickEdit and insert mode are switched off for the console at start; nothing else changes.
using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace DipGgFixes
{
    internal static class ConsoleGuard
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        const int STD_INPUT_HANDLE = -10;
        const uint ENABLE_QUICK_EDIT_MODE = 0x0040, ENABLE_INSERT_MODE = 0x0020, ENABLE_EXTENDED_FLAGS = 0x0080;

        internal static void Apply()
        {
            try
            {
                var h = GetStdHandle(STD_INPUT_HANDLE); if (h == IntPtr.Zero || h == new IntPtr(-1)) return;
                uint mode; if (!GetConsoleMode(h, out mode)) return;
                uint wanted = (mode & ~(ENABLE_QUICK_EDIT_MODE | ENABLE_INSERT_MODE)) | ENABLE_EXTENDED_FLAGS;
                if (wanted == mode) { MelonLogger.Msg("[DIP GG Fixes] console: QuickEdit already off"); return; }
                if (SetConsoleMode(h, wanted)) MelonLogger.Msg("[DIP GG Fixes] console: QuickEdit (select mode) switched off - a click in the console no longer freezes the game");
                else MelonLogger.Warning("[DIP GG Fixes] console: SetConsoleMode failed, error " + Marshal.GetLastWin32Error());
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes] console guard: " + e.Message); }
        }
    }
}
