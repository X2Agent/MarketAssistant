using Avalonia.Controls;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 修复 Windows 下中文输入法按 Shift 提交原始拼音时文本重复的问题（输入 free 后按 Shift 变成 freefree）。
/// <para>
/// 这是 Avalonia.Win32 的缺陷（上游 AvaloniaUI/Avalonia#20036，12.1.x 未修复），所有文本输入控件均受影响：
/// Avalonia 在 WM_IME_COMPOSITION 中已整串插入提交结果，消息却仍落入 DefWindowProc，
/// 后者会为结果串补发 WM_IME_CHAR，并最终变成逐字 WM_CHAR。Avalonia 依赖 _ignoreWmChar 忽略这些 WM_CHAR，
/// 但 Shift 是修饰键，输入法在 WM_KEYUP 时才提交，而 WM_KEYUP 会先把该标志清零，补发的字符便被当作新输入再插入一次。
/// Enter、空格在 WM_KEYDOWN 提交，不受影响。
/// </para>
/// 本类通过窗口消息钩子吞掉提交期间的 WM_IME_CHAR；非 Windows 平台上钩子注册为空操作。上游修复后删除本类及其调用。
/// </summary>
internal static class ImeCommitGuard
{
    private const uint WmKillFocus = 0x0008;
    private const uint WmImeStartComposition = 0x010D;
    private const uint WmImeEndComposition = 0x010E;
    private const uint WmImeComposition = 0x010F;
    private const uint WmImeChar = 0x0286;
    private const int GcsResultString = 0x0800;

    private static bool _isComposing;
    private static bool _resultInserted;

    /// <summary>
    /// 为此后每次显示的窗口挂接消息钩子，须在首个窗口显示前调用一次。
    /// </summary>
    public static void Install()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>(static (window, _) =>
        {
            // 窗口隐藏后再显示（如托盘恢复主窗口）会再次触发打开事件，先摘后挂避免重复注册
            Win32Properties.RemoveWndProcHookCallback(window, HandleWindowMessage);
            Win32Properties.AddWndProcHookCallback(window, HandleWindowMessage);
        });
    }

    private static IntPtr HandleWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmImeStartComposition:
                _isComposing = true;
                _resultInserted = false;
                break;
            case WmImeComposition:
                // 仅当组合进行中且携带结果串时 Avalonia 才会自行整串插入（与其 Imm32InputMethod 的判定一致）
                _resultInserted = _isComposing && (lParam & GcsResultString) != 0;
                break;
            case WmImeChar when _resultInserted:
                handled = true;
                break;
            case WmImeEndComposition:
            case WmKillFocus:
                _isComposing = false;
                _resultInserted = false;
                break;
        }

        return IntPtr.Zero;
    }
}
