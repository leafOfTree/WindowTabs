namespace Bemo
open System
open System.Threading

/// Whether WindowTabs keeps out of a full-screen app's way just now: its shortcuts let go of
/// their keys and its notices wait. Set on the main thread; the keyboard hook reads it too.
module FullScreenPause =
    let mutable private paused = 0
    let private changed = Event<bool>()

    let isPaused() = Volatile.Read(&paused)=1

    /// Told true as the pause starts and false as it ends.
    let changes = changed.Publish

    let set (value:bool) =
        if value<>isPaused() then
            Volatile.Write(&paused,(if value then 1 else 0))
            changed.Trigger value

    /// On while the setting is; then a notice also waits for a full-screen window on the main
    /// monitor, where it shows, though another monitor is in use and the shortcuts work.
    let mutable watching = false

    /// Read on the main thread, as a notice is asked for and while one waits.
    let coversNotices() =
        watching && FullScreenApps.CoversPrimaryMonitor(Diagnostics.Process.GetCurrentProcess().Id)
