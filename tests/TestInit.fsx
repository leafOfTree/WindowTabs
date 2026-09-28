module TestInit
// Runs before the script, like the first line of Bootstrap.main: SystemEvents gets a thread
// of its own instead of the main thread, whose top-level broadcast window otherwise receives
// other processes' broadcasts while the runtime shuts down and crashes the process.
Bemo.ThemeService.moveSystemEventsOffMainThread()
