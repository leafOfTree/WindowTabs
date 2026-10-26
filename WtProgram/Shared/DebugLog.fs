namespace Bemo
open System
open System.IO
open System.Text

/// Lines for working out what happened when it cannot be watched, such as a session restore
/// at sign-in: written beside the exe by Debug builds only, the newest last, the oldest
/// dropped past a size limit. Release builds write nothing.
module DebugLog =
    let fileName = "WindowTabsDebug.log"
    let private maxBytes = 1024L*1024L
    let private gate = obj()

    let path() = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,fileName)

    let write (text:string) =
#if DEBUG
        try
            lock gate (fun () ->
                let file = path()
                if File.Exists file && FileInfo(file).Length>maxBytes then
                    let kept = File.ReadAllLines(file,Encoding.UTF8)
                    File.WriteAllLines(file,kept.[kept.Length/2..],UTF8Encoding(false))
                let line = String.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}\r\n",DateTime.Now,Threading.Thread.CurrentThread.ManagedThreadId,text)
                File.AppendAllText(file,line,UTF8Encoding(false)))
        with _ -> ()
#else
        ignore text
#endif

    let writef format = Printf.kprintf write format
