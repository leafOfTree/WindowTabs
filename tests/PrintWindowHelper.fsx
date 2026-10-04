#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Bemo

type ProbeForm() =
    inherit Form(Text="Capture probe",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,
                 Location=Point(-20000,-20000),Size=Size(320,200),BackColor=Color.CornflowerBlue)
    let mutable delay = 0
    let mutable iconDelay = 0
    let mutable positionDelay = 0
    let mutable positionRequests = 0
    let mutable icon = SystemIcons.Information
    override _.ShowWithoutActivation = true
    override this.CreateParams =
        let parameters = base.CreateParams
        parameters.ExStyle <- parameters.ExStyle ||| 0x08000000 ||| 0x80
        parameters
    override this.WndProc(message:byref<Message>) =
        if message.Msg=0x804B then delay <- message.WParam.ToInt32()
        elif message.Msg=0x804C then iconDelay <- message.WParam.ToInt32()
        elif message.Msg=0x804D then icon <- (if message.WParam=IntPtr.Zero then SystemIcons.Information else SystemIcons.Warning)
        elif message.Msg=0x804E then positionDelay <- message.WParam.ToInt32()
        elif message.Msg=0x804F then positionRequests <- 0
        elif message.Msg=0x8050 then message.Result <- IntPtr(positionRequests)
        elif message.Msg=0x46 then
            positionRequests <- positionRequests+1
            Thread.Sleep(positionDelay)
            // This independent process must remain off screen when maximized.
            let position = Marshal.PtrToStructure(message.LParam,typeof<WINDOWPOS>) :?> WINDOWPOS
            position.flags <- position.flags ||| SetWindowPosFlags.SWP_NOACTIVATE
            if position.IsMove then position.x <- -20000; position.y <- -20000
            if position.IsSize then position.cx <- min position.cx 660; position.cy <- min position.cy 500
            Marshal.StructureToPtr(position,message.LParam,false)
            base.WndProc(&message)
        elif message.Msg=0x7F then
            printfn "ICON_REQUEST"
            Thread.Sleep(iconDelay)
            message.Result <- icon.Handle
            printfn "ICON_RESPONSE"
        elif message.Msg=0x0317 || message.Msg=0x0318 then
            printfn "PRINT_REQUEST"
            Thread.Sleep(delay)
            if message.WParam<>IntPtr.Zero then base.WndProc(&message)
        else base.WndProc(&message)

let main() =
    use form = new ProbeForm()
    use timeout = new System.Windows.Forms.Timer(Interval=30000)
    timeout.Tick.Add(fun _ -> form.Close())
    timeout.Start()
    form.Shown.Add(fun _ ->
        printfn "PROBE_HWND=%d" (form.Handle.ToInt64()))
    form.Show()
    while not form.IsDisposed do
        Application.DoEvents()
        Thread.Sleep(1)
TestInit.run main
