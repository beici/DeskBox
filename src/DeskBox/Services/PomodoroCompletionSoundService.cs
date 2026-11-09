using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>
/// 播放轻量的 Windows 系统提醒音，不引入媒体资源或常驻播放器。
/// P/Invoke 常驻 Platform 层的 Win32Helper(模块边界契约),本服务只保留
/// 别名回退链。
/// </summary>
internal static class PomodoroCompletionSoundService
{
    public static bool TryPlay() =>
        Win32Helper.TryPlaySystemSoundAlias("SystemNotification") ||
        Win32Helper.TryPlaySystemSoundAlias("SystemExclamation");
}
