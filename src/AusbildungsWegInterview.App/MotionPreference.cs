using Microsoft.Win32;

namespace AusbildungsWegInterview.App;

/// <summary>
/// Best-effort read of the Windows "Show animations in Windows" preference
/// (reduced-motion). Returns false (motion allowed) on any failure or on
/// non-Windows hosts — the visual transitions are all one-shot and cheap,
/// so the safe default is to keep them.
/// </summary>
public static class MotionPreference
{
    public static bool IsReduced
    {
        get
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    return false;
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var value = key?.GetValue("Animation") as string;
                return value == "0";
            }
            catch
            {
                return false;
            }
        }
    }
}
