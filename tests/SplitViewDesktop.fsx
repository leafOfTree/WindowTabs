#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Diagnostics
open System.Drawing
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Bemo

/// Split view on the real desktop, with real mouse input: Windows' own move-size loops, the
/// panes following live, stacking under the tabs and the empty pane's frame. Opt-in: it moves
/// the pointer, so it needs an idle, unlocked desktop (Run-Tests -VisibleDesktop).
module Native =
    [<DllImport("user32.dll")>]
    extern void mouse_event(uint32 flags, int dx, int dy, uint32 data, nativeint extra)
    [<DllImport("user32.dll")>]
    extern nativeint GetTopWindow(nativeint hwnd)
    [<DllImport("user32.dll")>]
    extern nativeint GetWindow(nativeint hwnd, uint32 command)
    [<DllImport("winmm.dll")>]
    extern uint32 timeBeginPeriod(uint32 period)
    [<DllImport("user32.dll",CharSet=CharSet.Unicode)>]
    extern nativeint FindWindow(string className, string title)

let check condition message = if not condition then failwith message
let pump milliseconds =
    let clock = Stopwatch.StartNew()
    while clock.ElapsedMilliseconds < int64 milliseconds do
        Application.DoEvents()
        Thread.Sleep(1)
/// Milliseconds until the predicate holds, or None after the limit.
let timeUntil limit predicate =
    let clock = Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && clock.ElapsedMilliseconds < int64 limit do
        Application.DoEvents()
        Thread.Sleep(1)
        satisfied <- predicate()
    if satisfied then Some clock.Elapsed.TotalMilliseconds else None
let pumpUntil description predicate =
    check (timeUntil 10000 predicate).IsSome description

/// An application window stand-in: sizable, activatable, a solid colour, and kept out of
/// the taskbar and Alt+Tab so a WindowTabs already running leaves it alone.
type PaneWindow(color:Color, title:string) =
    inherit Form(ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Text=title,BackColor=color,
                 FormBorderStyle=FormBorderStyle.Sizable,Location=Point(-20000,-20000),Size=Size(400,300))
    let mutable positions = 0
    let mutable delay = 0
    let mutable grid = 0
    member _.Positions = Volatile.Read(&positions)
    /// Rounds every width down to this step, as a terminal keeps to its character grid.
    member _.Grid with set value = grid <- value
    /// Each resize costs this long, as a heavy app's layout and paint do.
    member _.Delay with set value = delay <- value
    override this.CreateParams =
        let parameters = base.CreateParams
        parameters.ExStyle <- parameters.ExStyle ||| 0x80
        parameters
    override this.WndProc(message:Message byref) =
        if message.Msg=0x46 then
            Interlocked.Increment(&positions) |> ignore
            let position = Marshal.PtrToStructure(message.LParam,typeof<WINDOWPOS>) :?> WINDOWPOS
            if delay>0 && position.IsSize then Thread.Sleep(delay)
            if grid>0 && position.IsSize then
                position.cx <- position.cx-position.cx%grid
                Marshal.StructureToPtr(position,message.LParam,false)
        // Stand in for a native window that ignores WM_DPICHANGED, as the group places it.
        if message.Msg <> 0x2E0 then base.WndProc(&message)

let percentile (values:float list) (p:float) =
    match List.sort values with
    | [] -> 0.0
    | sorted -> sorted.[min (sorted.Length-1) (int(float sorted.Length*p))]

let main() =
    Native.timeBeginPeriod 1u |> ignore
    let screen = Screen.PrimaryScreen
    check (screen.WorkingArea.Width>=1400 && screen.WorkingArea.Height>=900) "The primary working area is too small for the split scenario"
    let original = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","split-desktop-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    use settings = new Settings(true)
    let api = settings :> ISettings
    api.setValue("autoHideMode",box "Never")
    let mouse = Event<int * IntPtr>()
    Services.register<IProgram>({new IProgram with
        member _.version = "test"
        member _.isFirstRun = false
        member _.refresh() = ()
        member _.regroupApplications _ = ()
        member _.shutdown() = ()
        member _.setWindowNameOverride _ = ()
        member _.getWindowNameOverride _ = None
        member _.getTabColorOverride _ = None
        member _.setTabColorOverride _ = ()
        member _.getTabColor(_,_) = None
        member _.getTabColors(hwnds,_) = hwnds |> List.map(fun _ -> None)
        member _.handMadeGroups() = []
        member _.ungroupHandMade _ = ()
        member _.groupIcon _ = None
        member _.setGroupIcon(_,_) = ()
        member _.appWindows = List2()
        member _.getAutoGroupingEnabled _ = false
        member _.setAutoGroupingEnabled _ _ = ()
        member _.tabAppearanceInfo = ThemeService.currentAppearance()
        member _.setHotKey _ _ = true
        member _.getHotKey _ = 0
        member _.newTab _ = ()
        member _.suspendTabMonitoring() = ()
        member _.resumeTabMonitoring() = ()
        member _.llMouse = mouse.Publish
        member _.paneCandidates() = []})
    Services.register<IFilterService>({new IFilterService with
        member _.isAppWindow _ = false
        member _.isAppWindowStyle _ = false
        member _.isTabbableWindow _ = false
        member _.isTabbingEnabledForAllProcessesByDefault with get()=false and set _=()
        member _.setIsTabbingEnabledForProcess _ _ = ()
        member _.setIsTabbingEnabledForProcesses _ _ = ()
        member _.getIsTabbingEnabledForProcess _ = false})
    let desktop = Desktop({new IDesktopNotification with
                            member _.dragDrop _ = ()
                            member _.dragEnd() = ()},api,InvokerService.invoker :> IDispatcher)
    let info = (desktop :> IDesktop).createGroup(false) :?> GroupInfo
    let onGroup action =
        let mutable result = None
        info.invokeGroup(fun () -> result <- Some(try Choice1Of2(action info.group) with e -> Choice2Of2 e))
        pumpUntil "The group did not answer" (fun () -> result.IsSome)
        match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
    let forms,dispatcher = ThreadHelper.startOnThreadAndWait(fun () ->
        let forms = [PaneWindow(Color.FromArgb(40,90,160),"Split A"); PaneWindow(Color.FromArgb(150,70,40),"Split B"); PaneWindow(Color.FromArgb(60,130,70),"Split C")]
        for form in forms do form.Handle |> ignore
        forms,InvokerService.invoker)
    let onHelper action =
        let mutable result = None
        dispatcher.asyncInvoke(fun () -> result <- Some(try Choice1Of2(action()) with error -> Choice2Of2 error))
        pumpUntil "The helper thread did not answer" (fun () -> result.IsSome)
        match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
    let a,b,c = forms.[0],forms.[1],forms.[2]
    let ha,hb,hc = onHelper(fun () -> a.Handle,b.Handle,c.Handle)
    let flags = BindingFlags.Instance ||| BindingFlags.NonPublic
    let transitionTimer (group:WindowGroup) =
        let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="minimizeStateTimer")
        field.GetValue(group) :?> System.Windows.Forms.Timer
    let waitOperation() =
        pumpUntil "Placement did not settle" (fun () -> onGroup(fun group -> group.isPlacementIdle && not((transitionTimer group).Enabled)))
        pump 150
    let os = OS()
    let visible hwnd = os.windowFromHwnd(hwnd).visibleBounds
    let bounds hwnd = os.windowFromHwnd(hwnd).bounds
    let near (x:int) (y:int) = abs(x-y)<=1
    /// Place in the top-level z-order, 0 at the top.
    let zIndex (hwnd:nativeint) =
        let rec walk (current:nativeint) index =
            if current=IntPtr.Zero then Int32.MaxValue
            elif current=hwnd then index
            else walk (Native.GetWindow(current,2u)) (index+1)
        walk (Native.GetTopWindow(IntPtr.Zero)) 0

    // Real input. The pointer is checked before each step: anyone else moving it ends the run.
    let primary = screen.Bounds
    let mutable expected : Point option = None
    let moveTo (x:int) (y:int) =
        expected |> Option.iter(fun at ->
            let now = Cursor.Position
            if abs(now.X-at.X)>3 || abs(now.Y-at.Y)>3 then failwith "The pointer moved during the scenario; leave the desktop idle")
        let nx = (x-primary.Left)*65535/max 1 (primary.Width-1)
        let ny = (y-primary.Top)*65535/max 1 (primary.Height-1)
        Native.mouse_event(0x8001u,nx,ny,0u,IntPtr.Zero)
        expected <- Some(Point(x,y))
    let down() = Native.mouse_event(0x0002u,0,0,0u,IntPtr.Zero)
    let up() = Native.mouse_event(0x0004u,0,0,0u,IntPtr.Zero)
    let click x y =
        moveTo x y
        pump 60
        down()
        pump 40
        up()
        pump 120
    /// Drags from a point in steps. Each step waits for the dragged window to change, then times
    /// how long `settled` takes to say the panes caught up again.
    let drag name (x:int) (y:int) (dx:int) (dy:int) steps (dragged:unit -> Rect) (settled:unit -> bool) =
        moveTo x y
        pump 120
        down()
        pump 80
        let lags = ResizeArray<float>()
        let responses = ResizeArray<float>()
        let mutable missed = 0
        let mutable unmoved = 0
        for step in 1..steps do
            let before = dragged()
            moveTo (x+dx*step/steps) (y+dy*step/steps)
            match timeUntil 300 (fun () -> dragged()<>before) with
            | None -> unmoved <- unmoved+1
            | Some response ->
                responses.Add response
                match timeUntil 400 settled with
                | Some lag -> lags.Add lag
                | None -> missed <- missed+1
            pump 10
        up()
        check (unmoved<=steps/4) (sprintf "%s: the drag moved nothing at %d of %d steps" name unmoved steps)
        let answered = List.ofSeq responses
        printfn "METRIC %s pointer-to-edge median=%.1fms p95=%.1fms" name (percentile answered 0.5) (percentile answered 0.95)
        if not(name.StartsWith "probe") then
            check (percentile answered 0.95<60.0) (sprintf "%s: the dragged edge trailed the pointer by %.0fms" name (percentile answered 0.95))
        let values = List.ofSeq lags
        printfn "METRIC %s steps=%d caught-up=%d median=%.1fms p95=%.1fms max=%.1fms" name steps values.Length (percentile values 0.5) (percentile values 0.95) (if values.IsEmpty then 0.0 else List.max values)
        check (missed<=steps/10) (sprintf "%s: the other pane fell behind at %d of %d steps" name missed steps)
        check (percentile values 0.95<200.0) (sprintf "%s: following took %.0fms at the 95th percentile" name (percentile values 0.95))

    try
        let work = screen.WorkingArea
        let frame = Rectangle(work.Left+work.Width/8,work.Top+work.Height/5,work.Width*3/4,work.Height*3/5)
        onHelper(fun () -> for form in forms do form.Bounds <- frame; form.Show())
        for hwnd in [ha;hb;hc] do (info :> IGroup).addWindow(hwnd,false)
        pumpUntil "Windows did not join" (fun () -> onGroup(fun group -> group.windows.count=3))
        onHelper(fun () -> a.Activate())
        pump 300
        waitOperation()
        let strip = onGroup(fun group -> group.ts.hwnd)
        let paneOf () = onGroup(fun group -> group.emptyPaneHwnds |> List.tryHead |> Option.defaultValue IntPtr.Zero)

        // An empty pane beside A: touching, below the strip, framed as Explorer is.
        let fullWidth = (visible ha).width
        onGroup(fun group -> group.execute(NewEmptyPane SideBySide,current=ha))
        // It opens out from the right edge over A, which keeps the whole frame until it has.
        let samples = ResizeArray<int * int>()
        let opening = Stopwatch.StartNew()
        while opening.ElapsedMilliseconds < 400L do
            let pane = paneOf()
            if pane<>IntPtr.Zero && WinUserApi.IsWindowVisible(pane) then samples.Add((bounds pane).width,(visible ha).width)
            pump 15
        waitOperation()
        let pane = paneOf()
        if SystemInformation.UIEffectsEnabled then
            let widths = samples |> Seq.map fst |> List.ofSeq
            let finalWidth = (bounds pane).width
            printfn "METRIC empty-pane-opening widths=%A" widths
            check (widths.Length>=5) "The empty pane opened in too few frames to see"
            check (List.head widths < finalWidth/3) (sprintf "The empty pane did not start narrow: %d of %d" (List.head widths) finalWidth)
            check (widths |> List.pairwise |> List.forall(fun (a,b) -> b>=a)) "The empty pane shrank while opening"
            let whileOpening = samples |> Seq.filter(fun (width,_) -> width < finalWidth*9/10) |> Seq.map snd |> List.ofSeq
            check (whileOpening |> List.forall(fun width -> width=fullWidth)) "A was resized while the empty pane was still opening"
        check (pane<>IntPtr.Zero && WinUserApi.IsWindowVisible(pane)) "No empty pane appeared"
        let aligned () =
            let left,right = visible ha,bounds pane
            near left.right right.left && near left.top right.top && near left.bottom right.bottom
        check (aligned()) (sprintf "The empty pane does not meet A: A=%A pane=%A" (visible ha) (bounds pane))
        check (zIndex strip < zIndex ha && zIndex strip < zIndex pane) "The tabs are not above both panes"
        check ((bounds strip).right > (bounds pane).left) (sprintf "Above the windows the tabs do not span the group: strip=%A pane=%A" (bounds strip) (bounds pane))
        let edge =
            let rect = bounds pane
            use bitmap = new Bitmap(1,1)
            use graphics = Graphics.FromImage(bitmap)
            graphics.CopyFromScreen(rect.x+rect.width/2,rect.y,0,0,Size(1,1))
            bitmap.GetPixel(0,0)
        let wanted = EmptySplitPane.frameColor()
        check (abs(int edge.R-int wanted.R)<=16 && abs(int edge.G-int wanted.G)<=16 && abs(int edge.B-int wanted.B)<=16)
              (sprintf "The empty pane's frame is %A, not %A" edge wanted)

        /// The drag's edge went about as far as the pointer did.
        let travelled name (actual:int) (wanted:int) =
            check (abs(actual-wanted)<=Dpi.scale 10) (sprintf "%s: the edge went %d, not about %d" name actual wanted)
        // Its inner edge, where the divider's band lies over it: A follows the ratio.
        let rect = bounds pane
        let positionsBefore = a.Positions
        drag "empty-inner-edge" (rect.left+Dpi.scale 5) (rect.y+rect.height/2) (-200) 0 20 (fun () -> bounds pane) aligned
        waitOperation()
        travelled "empty-inner-edge" ((bounds pane).left-rect.left) (-200)
        check (aligned()) (sprintf "After the inner-edge drag the panes do not meet: A=%A pane=%A" (visible ha) (bounds pane))
        printfn "METRIC empty-inner-edge positions-of-A=%d" (a.Positions-positionsBefore)
        let behind = visible hc
        check (behind=visible ha) (sprintf "The tab behind did not come back under A: C=%A A=%A" behind (visible ha))

        // Its top and bottom edges: the frame changes and A follows.
        let rect = bounds pane
        drag "empty-top-edge" (rect.x+rect.width/2) (rect.top+Dpi.scale 3) 0 (-80) 16 (fun () -> bounds pane) aligned
        waitOperation()
        travelled "empty-top-edge" ((bounds pane).top-rect.top) (-80)
        check (aligned()) "After the top-edge drag the panes do not meet"
        let rect = bounds pane
        drag "empty-bottom-edge" (rect.x+rect.width/2) (rect.bottom-Dpi.scale 2) 0 60 12 (fun () -> bounds pane) aligned
        waitOperation()
        travelled "empty-bottom-edge" ((bounds pane).bottom-rect.bottom) 60
        check (aligned()) "After the bottom-edge drag the panes do not meet"
        check (zIndex strip < zIndex ha && zIndex strip < zIndex pane) "After the drags the tabs are not above both panes"

        // Beside a heavy app, 40ms a resize: the pane keeps up with the pointer, and the app
        // trails by about one resize rather than falling further behind with each step.
        onHelper(fun () -> a.Delay <- 40)
        let rect = bounds pane
        drag "empty-inner-edge-slow-app" (rect.left+Dpi.scale 5) (rect.y+rect.height/2) 160 0 20 (fun () -> bounds pane) aligned
        waitOperation()
        travelled "empty-inner-edge-slow-app" ((bounds pane).left-rect.left) 160
        check (aligned()) "Beside a slow app the panes do not meet after the drag"
        let rect = bounds pane
        drag "empty-top-edge-slow-app" (rect.x+rect.width/2) (rect.top+Dpi.scale 3) 0 60 12 (fun () -> bounds pane) aligned
        waitOperation()
        check (aligned()) "Beside a slow app the panes do not meet after the top-edge drag"
        onHelper(fun () -> a.Delay <- 0)

        // Its outer edge: the frame grows, A stays.
        let rect = bounds pane
        let before = visible ha
        drag "empty-outer-edge" (rect.right-Dpi.scale 2) (rect.y+rect.height/2) 120 0 12 (fun () -> bounds pane) aligned
        waitOperation()
        travelled "empty-outer-edge" ((bounds pane).right-rect.right) 120
        check (aligned() && visible ha=before) "The outer-edge drag moved A"

        // B fills the pane; dragging B's outer edge natively, and then the divider.
        onGroup(fun group ->
            match group.splitLayout with
            | Some _ -> group.execute(FillPane(hb,[Second]),current=ha)
            | None -> failwith "The split ended")
        waitOperation()
        let meet () = near (visible ha).right (visible hb).left && near (visible ha).top (visible hb).top
        check (meet()) (sprintf "B did not take the pane: A=%A B=%A" (visible ha) (visible hb))
        check (paneOf()=IntPtr.Zero) "The empty pane stayed after B filled it"
        let right = visible hb
        drag "window-outer-edge" (right.right+Dpi.scale 3) (right.y+right.height/2) (-100) 0 10 (fun () -> visible hb) meet
        waitOperation()
        travelled "window-outer-edge" ((visible hb).right-right.right) (-100)
        check (meet()) "After B's own resize the panes do not meet"
        let divider = onGroup(fun group ->
            let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="splitDividers")
            snd (field.GetValue(group) :?> Map<PanePath,SplitDivider * IntPtr>).[[]])
        // At the seam only the bar takes the pointer; either side of it the panes' own resize
        // borders do, so a drag there runs in the app's own loop and paints at every step. A band
        // covering the borders took that away, and every drag at the seam trailed the pointer.
        do
            let band = bounds divider
            let y = band.y+band.height/2
            let onBar = [band.left..band.right-1] |> List.filter(fun x -> WinUserApi.WindowFromPoint(POINT(x,y))<>divider)
            let beside = [band.left-3;band.right+2] |> List.filter(fun x -> WinUserApi.WindowFromPoint(POINT(x,y))=divider)
            printfn "METRIC divider-band band=%A" band
            check (onBar.IsEmpty && beside.IsEmpty)
                  (sprintf "The divider's band is not the bar alone: bar missed at %A, taken beside it at %A" onBar beside)
        let band = bounds divider
        let seam = (visible ha).right
        drag "divider" (band.x+band.width/2) (band.y+band.height/2) 150 0 15 (fun () -> visible ha) meet
        waitOperation()
        travelled "divider" ((visible ha).right-seam) 150
        check (meet()) "After the divider drag the panes do not meet"
        let hostPane = onGroup(fun group -> let s = group.splitLayout.Value in SplitView.window (SplitView.host s) s |> Option.get)
        check (visible hc=visible hostPane) (sprintf "The tab behind did not come back under the host pane: C=%A host=%A" (visible hc) (visible hostPane))

        // Clicking either pane leaves the tabs over both.
        for hwnd in [ha;hb;ha] do
            let target = visible hwnd
            click (target.x+target.width/2) (target.y+target.height/2)
            waitOperation()
            check (zIndex strip < zIndex ha && zIndex strip < zIndex hb)
                  (sprintf "After clicking %A the tabs are under a pane: strip=%d A=%d B=%d" hwnd (zIndex strip) (zIndex ha) (zIndex hb))
        // Moving quickly between the panes: the tab behind neither moves nor shows over a pane.
        let behindAt = visible hc
        let behindMoves = c.Positions
        let mutable shown = 0
        for step in 1..8 do
            let target = visible (if step%2=1 then hb else ha)
            click (target.x+target.width/2) (target.y+target.height/2)
            for _ in 1..6 do
                if zIndex hc < zIndex ha || zIndex hc < zIndex hb then shown <- shown+1
                pump 10
        waitOperation()
        printfn "METRIC quick-switch shown-over-pane=%d moves-of-tab-behind=%d" shown (c.Positions-behindMoves)
        check (shown=0) (sprintf "The tab behind showed over a pane %d times while moving between the panes" shown)
        check (visible hc=behindAt && c.Positions=behindMoves) "Moving between the panes moved the tab behind"
        // Leaving the split for the tab behind, by its tab: the tabs stay over it the whole time.
        // Parked before the tab was in front, a tab behind showed for a moment, and the strip went
        // back to the pane under the rising tab.
        let tabAt hwnd = onGroup(fun group ->
            let location = group.ts.tabLocation(Tab(hwnd))
            os.windowFromHwnd(group.ts.hwnd).bounds.location.add(location).add(Pt(Dpi.scale 24,Dpi.scale 12)))
        for _ in 1..3 do
            let at = tabAt hc
            click at.x at.y
            let mutable covered = 0
            let clock = Stopwatch.StartNew()
            while clock.ElapsedMilliseconds < 600L do
                if os.foreground.hwnd=hc && zIndex strip > zIndex hc then covered <- covered+1
                pump 2
            waitOperation()
            printfn "METRIC leave-split covered-samples=%d" covered
            check (covered=0) (sprintf "Leaving the split for a tab behind put the tabs under it %d times" covered)
            check (onGroup(fun group -> match group.layout with Parked _ -> true | _ -> false)) "Leaving the split by a tab behind did not park it"
            let back = tabAt ha
            click back.x back.y
            waitOperation()
            check (onGroup(fun group -> group.splitLayout.IsSome)) "Choosing a pane's tab did not show the split again"
        // At the top of the screen the tabs sit inside the title bars. An empty pane opened from a
        // tab behind: once it has opened, the tabs are over every window of the group, that tab
        // fills the left pane and the others wait under it.
        onGroup(fun group -> group.execute(EndSplit None))
        waitOperation()
        onHelper(fun () -> a.Activate())
        pump 200
        os.windowFromHwnd(ha).move(Rect(Pt(frame.X,work.Top),Sz(frame.Width,frame.Height)))
        pump 300
        waitOperation()
        // As apps that refuse a foreign owner do (Windows Terminal among them): the strip is then
        // owned by nothing, and only restacking keeps it over the panes.
        onGroup(fun group ->
            let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="inputOwnership")
            let ownership = field.GetValue(group) :?> Collections.Generic.Dictionary<IntPtr,bool>
            for hwnd in [ha;hb;hc] do ownership.[hwnd] <- false)
        let inside = onGroup(fun group -> group.ts.direction=TabDown)
        printfn "METRIC tabs-inside=%b strip=%A A=%A" inside (bounds strip) (visible ha)
        onGroup(fun group -> group.execute(NewEmptyPane SideBySide,current=hb))
        pump 500
        waitOperation()
        let s = onGroup(fun group -> group.splitLayout.Value)
        check (s.tree.first=Leaf(Occupied hb)) "The tab the empty pane was opened from is not in the left pane"
        let left = visible hb
        for hwnd in [ha;hc] do
            check (visible hwnd=left) (sprintf "A tab behind is not under the left pane: %A=%A left=%A" hwnd (visible hwnd) left)
        let strip = onGroup(fun group -> group.ts.hwnd)
        let order = [ha;hb;hc] |> List.map(fun hwnd -> hwnd,zIndex hwnd)
        check (order |> List.forall(fun (_,z) -> zIndex strip < z))
              (sprintf "A window is over the tabs after the empty pane opened: strip=%d %A" (zIndex strip) order)
        check (zIndex hb < zIndex ha && zIndex hb < zIndex hc) (sprintf "The left pane is not over the tabs behind it: %A" order)
        // A click raises the app by Windows' own doing; the tabs, owned by nothing, come back over it.
        let target = visible hb
        click (target.x+target.width/2) (target.y+target.height/2)
        waitOperation()
        check (zIndex strip < zIndex hb) (sprintf "After a click on the left pane its window is over the tabs: strip=%d B=%d" (zIndex strip) (zIndex hb))
        // Inside the title bars too the tabs span the whole group, over the left pane's buttons.
        let tabs,leftPane = bounds strip,visible hb
        printfn "METRIC tabs-inside-split strip=%A left=%A" tabs leftPane
        check (tabs.x>=leftPane.x-Dpi.scale 2 && tabs.right>leftPane.right)
              (sprintf "Inside the title bars the tabs keep to the left pane: strip=%A left=%A" tabs leftPane)

        // A window on a character grid never takes its pane's exact width: the split goes on, with
        // no message, and the panes do not shrink step by step chasing its rounding.
        onGroup(fun group -> group.execute(EndSplit None))
        waitOperation()
        // A window coming into a split moves in smoothly: for a moment a cover over the group
        // moves the windows' images to their panes, and goes once the windows are in place.
        do
            let frame = onGroup(fun group -> group.layoutFrame)
            let pid = Process.GetCurrentProcess().Id
            let covers () =
                os.windowsInZorder.list |> List.exists(fun window ->
                    let mutable owner = 0
                    WinUserApi.GetWindowThreadProcessId(window.hwnd,&owner) |> ignore
                    owner=pid && window.isVisible && window.bounds=frame &&
                    (WinUserApi.GetWindowLong(window.hwnd,WindowLongFieldOffset.GWL_EXSTYLE).ToInt64() &&& int64 WindowsExtendedStyles.WS_EX_TOPMOST)<>0L)
            onGroup(fun group -> group.execute(SplitWith(hc,SideBySide),current=ha))
            check (covers()) "No cover moved the windows into the new split"
            let gone = timeUntil 3000 (fun () -> not(covers()))
            printfn "METRIC split-transition cover-gone-after=%A" gone
            check gone.IsSome "The cover over a new split stayed after its windows were in place"
            waitOperation()
            // Panes changing places move over smoothly too, and so does the window kept when the
            // split ends, growing to the whole group while the other fades.
            onGroup(fun group -> group.execute(Swap None))
            check (covers()) "No cover moved the panes as they swapped"
            check (timeUntil 3000 (fun () -> not(covers()))).IsSome "The cover over swapped panes stayed"
            waitOperation()
            onGroup(fun group -> group.execute(EndSplit(Some ha)))
            check (covers()) "No cover moved the window kept as the split ended"
            let ended = timeUntil 3000 (fun () -> not(covers()))
            printfn "METRIC split-end-transition cover-gone-after=%A" ended
            check ended.IsSome "The cover over an ended split stayed"
            waitOperation()
            check (visible ha=frame) (sprintf "The window kept did not fill the group after the split ended: %A of %A" (visible ha) frame)
            // While a tab is dragged the layout changes at once, with no cover: the transition's
            // start, on the thread the drag runs on, paused the drag.
            onGroup(fun group -> group.transitionsPaused <- true; group.execute(SplitWith(hc,SideBySide),current=ha))
            check (not(covers())) "A split started during a tab drag put a cover over the group"
            waitOperation()
            onGroup(fun group -> group.transitionsPaused <- false; group.execute(EndSplit(Some ha)))
            waitOperation()
        onHelper(fun () -> a.Grid <- 17)
        onGroup(fun group -> group.execute(SplitWith(hc,SideBySide),current=ha))
        pump 3000
        let early = onGroup(fun group -> group.splitLayout.IsSome),(visible ha).width,(visible hc).width
        pump 1500
        let late = onGroup(fun group -> group.splitLayout.IsSome),(visible ha).width,(visible hc).width
        printfn "METRIC grid-app early=%A late=%A" early late
        check (let still,_,_ = late in still) "A window on a character grid ended the split"
        check (early=late) (sprintf "The panes kept resizing beside a window on a character grid: %A then %A" early late)
        onHelper(fun () -> a.Grid <- 0)

        // Beside a window of another process, slow to resize as Explorer is: the empty pane's own
        // drag must not wait for that process. A second group keeps it apart from A, B and C.
        onGroup(fun group -> group.execute(EndSplit None))
        waitOperation()
        let script =
            "Add-Type -AssemblyName System.Windows.Forms;" +
            "$f=New-Object System.Windows.Forms.Form;$f.Text='Split External';$f.ShowInTaskbar=$false;" +
            "$f.FormBorderStyle='SizableToolWindow';$f.StartPosition='Manual';$f.BackColor='DarkSlateGray';" +
            "$f.Location=New-Object System.Drawing.Point(-20000,-20000);" +
            "$f.add_Resize({Start-Sleep -Milliseconds 40});[System.Windows.Forms.Application]::Run($f)"
        use external = Process.Start(ProcessStartInfo("powershell.exe","-NoProfile -Command \""+script+"\"",UseShellExecute=false,CreateNoWindow=true))
        try
            let mutable hx = IntPtr.Zero
            // Not in the taskbar, so it is no main window of its process: found by its title.
            pumpUntil "The other process's window did not open" (fun () ->
                hx <- Native.FindWindow(null,"Split External")
                hx<>IntPtr.Zero && WinUserApi.IsWindowVisible(hx))
            let other = (desktop :> IDesktop).createGroup(false) :?> GroupInfo
            let onOther action =
                let mutable result = None
                other.invokeGroup(fun () -> result <- Some(try Choice1Of2(action other.group) with e -> Choice2Of2 e))
                pumpUntil "The second group did not answer" (fun () -> result.IsSome)
                match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
            os.windowFromHwnd(hx).move(Rect(Pt(frame.X,frame.Y),Sz(frame.Width,frame.Height)))
            (other :> IGroup).addWindow(hx,false)
            pumpUntil "The other process's window did not join" (fun () -> onOther(fun group -> group.windows.count=1))
            let settle() =
                pumpUntil "The second group did not settle" (fun () -> onOther(fun group -> group.isPlacementIdle))
                pump 300
            settle()
            onOther(fun group -> group.execute(NewEmptyPane SideBySide,current=hx))
            settle()
            let pane = onOther(fun group -> group.emptyPaneHwnds |> List.tryHead |> Option.defaultValue IntPtr.Zero)
            check (pane<>IntPtr.Zero) "No empty pane beside the other process's window"
            let besideOther () = near (visible hx).right (bounds pane).left && near (visible hx).top (bounds pane).top
            check (besideOther()) (sprintf "The empty pane does not meet the other process's window: %A %A" (visible hx) (bounds pane))
            // As in use: the app is clicked first, which raises it, and then the pane's edge dragged.
            let app = visible hx
            click (app.x+app.width/2) (app.y+app.height/2)
            settle()
            let rect = bounds pane
            let grab = POINT(rect.left+Dpi.scale 5,rect.y+rect.height/2)
            // The app's invisible resize border reaches over the pane's edge: the pane must be on top
            // of it, or the drag takes the slow app's own edge instead.
            let taken = WinUserApi.WindowFromPoint grab
            let dividerHere = onOther(fun group ->
                let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="splitDividers")
                (field.GetValue(group) :?> Map<PanePath,SplitDivider * IntPtr>).TryFind [] |> Option.map snd |> Option.defaultValue IntPtr.Zero)
            check (zIndex pane < zIndex hx && (taken=pane || taken=dividerHere))
                  (sprintf "The empty pane's edge is under the other process's window: pane=%d other=%d, pointer on %A" (zIndex pane) (zIndex hx) taken)
            drag "cross-process-inner-edge" (rect.left+Dpi.scale 5) (rect.y+rect.height/2) (-160) 0 16 (fun () -> bounds pane) besideOther
            settle()
            check (besideOther()) "After the drag the empty pane does not meet the other process's window"
            let rect = bounds pane
            drag "cross-process-top-edge" (rect.x+rect.width/2) (rect.top+Dpi.scale 3) 0 (-60) 12 (fun () -> bounds pane) besideOther
            settle()
            check (besideOther()) "After the top-edge drag the empty pane does not meet the other process's window"
            // Both panes windows of other processes, as Explorer beside Notepad: the divider keeps up
            // with the pointer and the panes follow it.
            let script2 = script.Replace("Split External","Split External Two").Replace("Start-Sleep -Milliseconds 40","")
            use external2 = Process.Start(ProcessStartInfo("powershell.exe","-NoProfile -Command \""+script2+"\"",UseShellExecute=false,CreateNoWindow=true))
            try
                let mutable hy = IntPtr.Zero
                pumpUntil "The second process's window did not open" (fun () ->
                    hy <- Native.FindWindow(null,"Split External Two")
                    hy<>IntPtr.Zero && WinUserApi.IsWindowVisible(hy))
                onOther(fun group -> group.dropTab(hy,FillEmpty [Second],hx))
                settle()
                let meet () = near (visible hx).right (visible hy).left && near (visible hx).top (visible hy).top
                check (meet()) (sprintf "The second process's window did not take the pane: %A %A" (visible hx) (visible hy))
                let divider = onOther(fun group ->
                    let field = typeof<WindowGroup>.GetFields(flags) |> Array.find(fun field -> field.Name="splitDividers")
                    snd (field.GetValue(group) :?> Map<PanePath,SplitDivider * IntPtr>).[[]])
                let app = visible hy
                click (app.x+app.width/2) (app.y+app.height/2)
                settle()
                let band = bounds divider
                let seam = (visible hx).right
                drag "cross-process-divider" (band.x+band.width/2) (band.y+band.height/2) 150 0 15 (fun () -> bounds divider) meet
                settle()
                check (abs((visible hx).right-seam-150)<=Dpi.scale 10 && meet()) (sprintf "The divider between two other processes went %d, not about 150" ((visible hx).right-seam))
                // Again with the slow app in focus, so the tabs belong to it.
                let app = visible hx
                click (app.x+app.width/2) (app.y+app.height/2)
                settle()
                let band = bounds divider
                drag "cross-process-divider-slow-focused" (band.x+band.width/2) (band.y+band.height/2) (-150) 0 15 (fun () -> bounds divider) meet
                settle()
                check (meet()) "After the divider drag beside the focused slow app the panes do not meet"
                // As fast as a real pointer reports, about every 1.5ms: the divider keeps up with it.
                // A ratio sent at every move piled up faster than the group placed them, and the
                // divider trailed the pointer by seconds.
                do
                    let band = bounds divider
                    let x,y = band.x+band.width/2,band.y+band.height/2
                    moveTo x y
                    pump 120
                    down()
                    pump 80
                    let start = (bounds divider).x
                    let steps,dx = 300,200
                    let mutable worst = 0
                    for step in 1..steps do
                        moveTo (x+dx*step/steps) y
                        let spin = Stopwatch.StartNew()
                        while spin.Elapsed.TotalMilliseconds < 1.5 do ()
                        if step%10=0 then worst <- max worst (abs(dx*step/steps-((bounds divider).x-start)))
                    let caught = timeUntil 3000 (fun () -> abs((bounds divider).x-start-dx)<=3)
                    up()
                    printfn "METRIC cross-process-divider-fast worst-behind=%dpx caught-up=%A" worst caught
                    check (worst<Dpi.scale 40) (sprintf "Dragged fast, the divider fell %dpx behind the pointer" worst)
                    check (caught |> Option.exists(fun ms -> ms<150.0)) (sprintf "Dragged fast, the divider reached the pointer only after %A ms" caught)
                    settle()
                    check (meet()) "After a fast divider drag the panes do not meet"
                // A window joining with the focus, as a tab dragged here has from its drag, from
                // outside the panes parks them and fills the group, as choosing its tab does. Placed
                // as a tab behind, it waited under the panes with the focus.
                do
                    onGroup(fun group -> group.removeWindow hc)
                    settle()
                    // Below the panes, where nothing of either group lies, so the click reaches it.
                    let spot = Rect(Pt(frame.X,frame.Bottom+10),Sz(300,140))
                    let joining = os.windowFromHwnd(hc)
                    joining.showWindow(ShowWindowCommands.SW_SHOWNORMAL)
                    joining.move(spot)
                    joining.raise()
                    pump 100
                    click (spot.x+spot.width/2) (spot.y+spot.height/2)
                    pumpUntil (sprintf "The joining window did not take the focus: visible=%b minimized=%b at %A z=%d, foreground %A" (WinUserApi.IsWindowVisible hc) joining.isMinimized (visible hc) (zIndex hc) (WinUserApi.GetForegroundWindow()))
                        (fun () -> WinUserApi.GetForegroundWindow()=hc)
                    let otherFrame = onOther(fun group -> group.layoutFrame)
                    (other :> IGroup).addWindow(hc,false)
                    pumpUntil "The joining window did not join" (fun () -> onOther(fun group -> group.windows.contains hc))
                    settle()
                    check (onOther(fun group -> (match group.layout with Parked _ -> true | _ -> false) && group.topWindow=hc))
                          (sprintf "A window joining with the focus did not park the panes: %A" (onOther(fun group -> group.layout)))
                    check (visible hc=otherFrame && zIndex hc < zIndex hx && zIndex hc < zIndex hy)
                          (sprintf "The joining window is not shown over the panes at the group's frame: %A, frame %A" (visible hc) otherFrame)
                    onOther(fun group -> group.removeWindow hc)
                    settle()
                    onOther(fun group -> group.execute FocusOther)
                    settle()
                    check (meet()) "The panes did not come back after the joined window left"
                onOther(fun group -> group.removeWindow hy)
                settle()
            finally
                if not external2.HasExited then external2.Kill()
            onOther(fun group -> group.execute(EndSplit None))
            settle()
            onOther(fun group -> group.removeWindow hx)
            (other :> IGroup).destroy()
        finally
            if not external.HasExited then external.Kill()
        printfn "PASS: empty pane frame, edges and stacking; window and divider drags with live following; tabs over both panes after clicks; beside another process."
    finally
        try onGroup(fun group -> group.execute(EndSplit None)) with _ -> ()
        pump 300
        for hwnd in [ha;hb;hc] do (try onGroup(fun group -> group.removeWindow hwnd) with _ -> ())
        (try (info :> IGroup).destroy() with _ -> ())
        (try onHelper(fun () -> for form in forms do form.Close()) with _ -> ())
        Environment.CurrentDirectory <- original
TestInit.run main
