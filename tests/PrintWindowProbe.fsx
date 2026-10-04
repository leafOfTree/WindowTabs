// A foreign helper process owns the captured window. No user windows are read.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let main() =
    use receiver = new Control()
    receiver.Handle |> ignore
    let options = ProcessStartInfo(Path.Combine(__SOURCE_DIRECTORY__,"Debug","PrintWindowHelper.exe"))
    options.UseShellExecute <- false
    options.CreateNoWindow <- true
    options.RedirectStandardOutput <- true
    options.RedirectStandardError <- true
    use helper = Process.Start(options)
    let output = Collections.Concurrent.ConcurrentQueue<string>()
    helper.OutputDataReceived.Add(fun args -> if not(isNull args.Data) then output.Enqueue(args.Data))
    helper.ErrorDataReceived.Add(fun args -> if not(isNull args.Data) then output.Enqueue(args.Data))
    helper.BeginOutputReadLine()
    helper.BeginErrorReadLine()
    let mutable hwnd = IntPtr.Zero
    try
        let startup = Stopwatch.StartNew()
        while hwnd=IntPtr.Zero && startup.ElapsedMilliseconds<5000L && not helper.HasExited do
            output.ToArray() |> Array.tryFind(fun line -> line.StartsWith("PROBE_HWND="))
            |> Option.iter(fun line -> hwnd <- IntPtr(Int64.Parse(line.Substring(11))))
            Thread.Sleep(10)
        check (hwnd<>IntPtr.Zero) ("Capture helper did not start: "+String.concat "\n" (output.ToArray()))
        let capture (delay:int) directPrintMessage =
            WinUserApi.SendMessage(hwnd,0x804B,IntPtr(delay),IntPtr.Zero) |> ignore
            let clock = Stopwatch.StartNew()
            let mutable markerDelay = -1.0
            receiver.BeginInvoke(Action(fun () -> markerDelay <- clock.Elapsed.TotalMilliseconds)) |> ignore
            let mutable succeeded = false
            use bitmap =
                if directPrintMessage then
                    // A caller HDC cannot be passed directly across processes.
                    // This control measures response delay without a drawing DC.
                    WinUserApi.SendMessage(hwnd,0x0317,IntPtr.Zero,IntPtr.Zero) |> ignore
                    succeeded <- true
                    new Drawing.Bitmap(320,200)
                else Win32Helper.PrintWindow(hwnd,&succeeded)
            let elapsed = clock.Elapsed.TotalMilliseconds
            Application.DoEvents()
            check (markerDelay>=0.0) "Queued UI work was lost"
            printfn "%s delay=%d ms: call %.2f ms; queued UI work waited %.2f ms; status=%s" (if directPrintMessage then "Direct WM_PRINT control" else "PrintWindow") delay elapsed markerDelay (if directPrintMessage then "response-completed" else sprintf "captured=%b" succeeded)
            elapsed,markerDelay
        capture 0 false |> ignore
        capture 250 false |> ignore
        let elapsed,queuedDelay = capture 250 true
        check (elapsed>=240.0 && queuedDelay>=150.0) "Synchronous print message did not block caller UI work"
        printfn "PASS: PrintWindow status/timing recorded separately from controlled slow WM_PRINT; no claim that every OS uses that print path."
    finally
        if hwnd<>IntPtr.Zero then WinUserApi.PostMessage(hwnd,WindowMessages.WM_CLOSE,IntPtr.Zero,IntPtr.Zero) |> ignore
        if not(helper.WaitForExit(5000)) then
            helper.Kill()
            helper.WaitForExit()
            failwith "Capture helper did not exit"
        helper.WaitForExit()
        printfn "Capture probe output: %s" (String.concat " | " (output.ToArray()))
        check (helper.ExitCode=0 && (output.ToArray() |> Array.contains "TEST_BODY_COMPLETE")) "Capture helper failed during rendering or cleanup"

TestInit.run main
