#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Diagnostics
open System.Drawing
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo

/// Opt-in real input against an editor containing only test draft rows.
module Input =
    [<DllImport("user32.dll")>]
    extern void mouse_event(uint32 flags,int dx,int dy,uint32 data,nativeint extra)
    [<DllImport("user32.dll")>]
    extern void keybd_event(byte key,byte scan,uint32 flags,nativeint extra)

let check condition message = if not condition then failwith message
let pump ms =
    let clock = Stopwatch.StartNew()
    while clock.ElapsedMilliseconds<int64 ms do
        Application.DoEvents()
        Threading.Thread.Sleep 2

let main() =
    use settings = new Settings(true)
    let a,b,c,d = IntPtr 11,IntPtr 22,IntPtr 33,IntPtr 44
    let groups = [[a;b;c];[d]] |> List.mapi(fun i tabs -> GroupDraft.ofGroup (IntPtr(i+1)) tabs Set.empty GroupLayout.Single)
    let frame = Rect(Pt(120,120),Sz(800,600))
    let rows = groups |> List.map(fun g -> {group=g.group; frame=frame; active=g.tabs.Head})
    let windows = [a;b;c;d] |> List.map(fun h -> {hwnd=h;name=sprintf "Test tab %d" (int h);icon=None;minimized=true})
    let editor = GroupEditorForm(DesktopDraft.ofDesktop groups [],rows,windows,None)
    let previous = Cursor.Position
    let centre (r:Rectangle) = Point(r.X+r.Width/2,r.Y+r.Height/2)
    let tab h = editor.Layout.rows |> List.collect(fun row -> row.tabs) |> List.find(fun (hwnd,_) -> hwnd=h) |> snd |> centre
    let move pt =
        Cursor.Position <- Control.FromHandle(editor.Handle).PointToScreen(pt)
        pump 80
        let hit = OS().windowFromHwnd(WinUserApi.WindowFromPoint(Cursor.Position.Pt.POINT))
        check (hit.hwnd=editor.Handle || editor.Drag.IsSome) (sprintf "Editor covered by %s; visible %b" hit.className (WinUserApi.IsWindowVisible editor.Handle))
    let button down = Input.mouse_event((if down then 2u else 4u),0,0,0u,0n); pump 60
    let key k down = Input.keybd_event(byte (int k),0uy,(if down then 0u else 2u),0n); pump 30
    let click k h =
        move (tab h)
        key k true
        try button true; button false finally key k false
    let leftEdge h =
        let rect = editor.Layout.rows |> List.collect(fun r -> r.tabs) |> List.find(fun (tab,_) -> tab=h) |> snd
        let pt = Control.FromHandle(editor.Handle).PointToScreen(Point(rect.Left,rect.Y+rect.Height/2))
        use pixels = new Bitmap(3,1)
        use g = Graphics.FromImage pixels
        g.CopyFromScreen(pt,Point.Empty,pixels.Size)
        [for x in 0..2 -> pixels.GetPixel(x,0).ToArgb()]
    try
        editor.Show()
        let form = Control.FromHandle(editor.Handle) :?> Form
        form.Hide()
        form.Show()
        form.TopMost <- true
        form.BringToFront()
        pump 180
        move (tab d)
        let unmarked = leftEdge a
        click Keys.ControlKey a
        click Keys.ControlKey c
        check (leftEdge a<>unmarked) "The first mark was not painted after the pointer left"
        check (editor.MarkedTabs=[a;c]) "Ctrl-click did not select disjoint tabs"
        click Keys.ControlKey a
        click Keys.ControlKey c
        check (leftEdge a=unmarked) "The deselected tab retained its painted outline"
        check editor.MarkedTabs.IsEmpty "Ctrl-click did not remove the last mark"
        click Keys.ControlKey a
        click Keys.ShiftKey c
        check (editor.MarkedTabs=[a;b;c]) "Shift did not extend the range"
        click Keys.ShiftKey b
        check (editor.MarkedTabs=[a;b]) "Shift did not shrink the range"
        move (tab b)
        key Keys.ControlKey true
        button true
        let last = tab d
        move (Point(last.X+80,last.Y))
        check (editor.Drag |> Option.exists(fun drag -> drag.begun)) "Real input did not start a drag"
        check (List.map fst editor.Layout.rows.[1].tabs=[d;a;b]) "The destination did not make room for the block before release"
        button false
        key Keys.ControlKey false
        check (editor.MarkedTabs.IsEmpty && editor.Drag.IsNone && (editor.Draft.groups |> List.map(fun g -> g.tabs))=[[c];[d;a;b]]) "Batch release retained its drag or selection"
        pump 250
        move (tab b)
        button true
        move (centre editor.Layout.newGroup)
        button false
        check ((editor.Draft.groups |> List.map(fun g -> g.tabs))=[[c];[d;a];[b]]) "The next unmodified drag carried the previous selection"
        pump 250
        click Keys.ControlKey d
        click Keys.ControlKey a
        // Holding Shift on a member must carry the noncontiguous Ctrl selection as-is.
        click Keys.ControlKey b
        key Keys.ShiftKey true
        move (tab a)
        button true
        move (centre editor.Layout.newGroup)
        check (editor.MarkedTabs=[d;a;b] && (editor.Drag |> Option.exists(fun drag -> drag.begun))) "Shift-drag replaced the selection with a click range"
        button false
        key Keys.ShiftKey false
        check (editor.Draft.groups |> List.last |> fun g -> g.tabs=[d;a;b]) "Shift-drag did not drop the whole selection"
        pump 250
        click Keys.ControlKey d
        click Keys.ControlKey a
        let beforeCancel = editor.Draft
        move (tab d)
        button true
        move (centre editor.Layout.newGroup)
        // Deliver logical Escape independently of desktop key remappers.
        WinUserApi.PostMessage(editor.Handle,WindowMessages.WM_KEYDOWN,IntPtr(int Keys.Escape),IntPtr.Zero) |> ignore
        pump 80
        button false
        check (editor.Drag.IsNone && editor.MarkedTabs.IsEmpty && editor.Draft=beforeCancel) "Escape left a selection or committed the drag"
        printfn "PASS: real Ctrl toggle, Shift extension and contraction, live block preview, drop and independent second drag."
    finally
        key Keys.ShiftKey false
        key Keys.ControlKey false
        Input.mouse_event(4u,0,0,0u,0n)
        editor.Close()
        Cursor.Position <- previous

TestInit.run main
