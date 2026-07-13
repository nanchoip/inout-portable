using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace InoutPortable.App;

/// <summary>
/// Opens a3ERP's own company selector by launching a3ERPInOut.exe, waiting until the user picks a
/// company (its main "InOut" window appears), reading the chosen company from the registry
/// (HKCU\Software\A3\A3ERP\"Empresa actual") and then closing a3ERP. No a3ERP runtime is reimplemented.
/// Based on the reverse-engineering experiment in A3ERP-InOut-Web\nativo\SelectorEmpresa.
/// </summary>
public static class NativeCompanySelector
{
    private const string RegSubKey = @"Software\A3\A3ERP";
    private const string RegValue = "Empresa actual";

    /// <summary>Launches a3ERP, waits for the selection, and returns the chosen company description (or null).</summary>
    public static Task<string?> CaptureAsync(string exePath, int timeoutSeconds = 180, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Process? proc = null;
            try
            {
                proc = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
            catch
            {
                return (string?)null;
            }
            if (proc is null) return null;

            string? captured = null;
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < deadline && !ct.IsCancellationRequested)
            {
                Thread.Sleep(700);
                try { proc.Refresh(); if (proc.HasExited) break; } catch { break; }

                if (HasWindowContaining((uint)proc.Id, "InOut"))
                {
                    Thread.Sleep(400); // let a3ERP persist the choice to the registry
                    captured = ReadEmpresaActual();
                    break;
                }
            }

            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }

            return captured ?? ReadEmpresaActual();
        }, ct);
    }

    public static string? ReadEmpresaActual()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegSubKey);
            return key?.GetValue(RegValue) as string;
        }
        catch
        {
            return null;
        }
    }

    // --- Win32: is there a visible window of this process whose title contains the text? ---

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static bool HasWindowContaining(uint pid, string text)
    {
        bool found = false;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var wpid);
            if (wpid != pid || !IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = true;
                return false; // stop enumerating
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
