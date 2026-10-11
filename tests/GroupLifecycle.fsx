#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
/// Checks the state it saw rather than asking again, which can catch a later change.
let pumpUntil description predicate =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && clock.ElapsedMilliseconds < 5000L do
        Application.DoEvents()
        Thread.Sleep(5)
        satisfied <- predicate()
    check satisfied description

let main() =
    check (TabTitle.display false (IntPtr(0x123)) "Editor"="Editor") "Normal tab titles contain a window handle"
    check (TabTitle.display true (IntPtr(0x123)) "Editor"="123 - Editor") "Debug builds lost the debugger-only window handle"
    Application.EnableVisualStyles()
    let directory = Path.Combine(__SOURCE_DIRECTORY__, "Debug", "groups-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(directory) |> ignore
    let previousDirectory = Environment.CurrentDirectory
    Environment.CurrentDirectory <- directory
    try
        use settings = new Settings(true)
        let api = settings :> ISettings
        api.setValue("enableCtrlNumberHotKey", box false)
        api.setValue("autoHideMode", box "Never")
        let mouse = Event<int * IntPtr>()
        let mutable workspaceWindows = List2<IntPtr>()
        Services.register<IProgram>({new IProgram with
            member _.version = "test"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.getTabColorOverride _ = None
            member _.setTabColorOverride _ = ()
            member _.getTabColor(_,_) = None
            member _.appWindows = workspaceWindows
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey _ = 0
            member _.newTab _ = ()
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish})
        let desktop = Desktop({new IDesktopNotification with
                                member _.dragDrop _ = ()
                                member _.dragEnd() = ()}, api, InvokerService.invoker :> IDispatcher)
        let desktopApi = desktop :> IDesktop
        let dragTargetFor hwnd =
            let field = typeof<DragDropController>.GetFields(Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
                        |> Array.find(fun field -> field.FieldType=typeof<Cell<Map2<IntPtr,IDragDropTarget>>>)
            let targets = field.GetValue(Services.dragDrop) :?> Cell<Map2<IntPtr,IDragDropTarget>>
            targets.value.find hwnd
        let source = desktopApi.createGroup(false) :?> GroupInfo
        let target = desktopApi.createGroup(false) :?> GroupInfo
        let combined = desktopApi.createGroup(true) :?> GroupInfo
        let onGroup (info:GroupInfo) action =
            let mutable result = None
            info.invokeGroup(fun () ->
                result <- Some(try Choice1Of2(action info.group) with error -> Choice2Of2 error))
            pumpUntil "Group dispatcher did not complete" (fun () -> result.IsSome)
            match result.Value with Choice1Of2 value -> value | Choice2Of2 error -> raise error
        let nativeOrder info = onGroup info (fun group -> group.lorder.list)
        let publicOrder (info:GroupInfo) = (info :> IGroup).windows.list
        api.setValue("tabPosition",box "Right")
        api.setValue("tabColumnWidth",box 280)
        let defaults = desktopApi.createGroup(false) :?> GroupInfo
        check (onGroup defaults (fun group -> group.ts.dock,group.ts.columnWidth)=(TabDockRight,280))
              "New group ignored the saved tab defaults"
        api.setValue("tabPosition",box "Left")
        api.setValue("tabColumnWidth",box 320)
        for info in [source;target;combined;defaults] do
            pumpUntil "Global layout settings did not update an existing group" (fun () ->
                onGroup info (fun group -> group.ts.dock,group.ts.columnWidth)=(TabDockLeft,320))
        (defaults :> IGroup).destroy()
        pumpUntil "Default layout group did not exit" (fun () -> defaults.isExited)
        api.setValue("tabPosition",box "Horizontal")
        api.setValue("tabColumnWidth",box 220)
        // Hidden real HWNDs avoid changing the user's foreground window. Exercise
        // the production group and tab-strip implementations on their own STAs.
        let forms = [ for name in ["First"; "Second"; "Third"] ->
                        new Form(Text=name, ShowInTaskbar=false,
                                 StartPosition=FormStartPosition.Manual,
                                 Location=Drawing.Point(-20000,-20000), Size=Drawing.Size(640,480)) ]
        let handles = forms |> List.map (fun form -> form.Handle)
        let a,b,c = handles.[0],handles.[1],handles.[2]
        try
            let restored = Collections.Generic.List<IntPtr>()
            onGroup source (fun _ -> (TaskbarRestorePlugin(restored.Add) :> IPlugin).init())
            for hwnd in handles do (source :> IGroup).addWindow(hwnd,false)
            pumpUntil "Native group did not acquire three windows" (fun () -> nativeOrder source = handles)
            pumpUntil "Desktop registry did not acquire three windows" (fun () -> publicOrder source = handles)
            check (onGroup source (fun _ -> restored |> Seq.toList) = handles)
                  "Startup grouping did not reclaim every ordinary taskbar button"
            let localLayout = {dock=TabDockRight;columnWidth=264;normalAlignment=TabRight;insideAlignment=TabCenter}
            onGroup source (fun group -> group.applyTabLayout localLayout)
            api.setValue("tabPosition",box "Left")
            pumpUntil "Global position did not replace the group's menu choice" (fun () ->
                onGroup source (fun group -> group.tabLayout)={localLayout with dock=TabDockLeft})
            api.setValue("tabColumnWidth",box 320)
            let updatedLayout = {localLayout with dock=TabDockLeft;columnWidth=320}
            pumpUntil "Global width did not replace the group's resized width" (fun () ->
                onGroup source (fun group -> group.tabLayout)=updatedLayout)
            check (handles |> List.forall(fun hwnd -> WindowTabLayout.read hwnd=Some updatedLayout))
                  "Live global settings left stale remembered window layouts"
            api.setValue("tabPosition",box "Horizontal")
            api.setValue("tabColumnWidth",box 220)
            pumpUntil "Switching back to Top did not update the live group" (fun () ->
                onGroup source (fun group -> group.ts.dock,group.ts.columnWidth)=(TabDockTop,220))
            onGroup source (fun group -> group.ts.setAlignment(TabUp,TabLeft);group.ts.setAlignment(TabDown,TabLeft))
            (source :> IGroup).addWindow(a,false)
            check (nativeOrder source = handles) "Duplicate add created a duplicate tab"
            check (publicOrder source = handles) "Duplicate add corrupted desktop membership"
            check (onGroup source (fun _ -> restored.Count) = 3) "Duplicate grouping repeatedly restored a taskbar button"
            (source :> IGroup).removeWindow b
            (source :> IGroup).addWindow(b,false)
            pumpUntil "Queued remove-then-add lost the window" (fun () -> nativeOrder source = [a;c;b] && publicOrder source = [a;c;b])
            onGroup source (fun group -> group.ts.moveTab(Tab(c),0))
            pumpUntil "Tab reorder did not reach desktop registry" (fun () -> publicOrder source = [c;a;b])
            check (nativeOrder source = [c;a;b]) "Native tab order differs from registry"

            // The detach completion is observed before attaching to the other STA.
            (source :> IGroup).removeWindow b
            pumpUntil "Detach did not remove source membership" (fun () -> publicOrder source = [c;a])
            (target :> IGroup).addWindow(b,false)
            pumpUntil "Attach did not update target membership" (fun () -> nativeOrder target = [b] && publicOrder target = [b])
            check (nativeOrder source = [c;a]) "Transfer changed unrelated source tabs"
            check ((desktop.findGroupContainingHwnd b).Value.hwnd = target.hwnd) "Transferred window resolves to wrong group"

            // A dead foreign HWND is removed by reconciliation; exercise that
            // removal after destruction rather than only while the HWND is valid.
            let beforeClose = onGroup source (fun _ -> restored.Count)
            forms.[2].Dispose()
            check (not(WinUserApi.IsWindow c)) "Helper window did not close"
            (source :> IGroup).removeWindow c
            pumpUntil "Closed window left a stale tab" (fun () -> nativeOrder source = [a] && publicOrder source = [a])
            onGroup source (fun group -> group.removeWindow(c))
            check (nativeOrder source = [a]) "Repeated removal damaged remaining tabs"
            check (onGroup source (fun _ -> restored.Count) = beforeClose) "Taskbar restoration used a destroyed window"

            for info in [source;target;combined] do
                for hwnd in publicOrder info do (info :> IGroup).removeWindow hwnd
                pumpUntil "Group did not empty" (fun () -> nativeOrder info = [] && publicOrder info = [])
            check (WinUserApi.IsWindow a && WinUserApi.IsWindow b) "Ungrouping closed application windows"

            // A new window is the group's foreground before it joins; its tab info
            // reaches the combined taskbar button before its tab reaches the strip.
            use joining = new Form(Text="Joining", ShowInTaskbar=false, StartPosition=FormStartPosition.Manual,
                                   Location=Drawing.Point(-20000,-20000), Size=Drawing.Size(640,480))
            let d = joining.Handle
            for hwnd in [a;b] do (combined :> IGroup).addWindow(hwnd,false)
            pumpUntil "Combined group did not acquire two windows" (fun () -> nativeOrder combined = [a;b])
            // Off screen, the group saves no placement; its previews still need the window's area.
            let area,top = onGroup combined (fun group -> group.previewArea, group.topWindow)
            check (not area.size.isEmptyArea && area = OS().windowFromHwnd(top).bounds) (sprintf "A group never on screen has no preview area: %A" area)
            // The window standing in for the group on the taskbar is an Alt+Tab window the switcher must skip.
            let proxies() = OS().windowsInZorder.where(fun w -> TaskbarProxies.contains w.hwnd).map(fun w -> w.hwnd).list
            let proxy = proxies() |> List.filter(fun hwnd -> OS().windowFromHwnd(hwnd).isAltTabWindow)
            check (proxy.Length=1) "Combined group's taskbar window is not known as a taskbar proxy"
            onGroup combined (fun group ->
                group.foreground <- d
                group.addWindow(d,false))
            check (nativeOrder combined = [a;b;d]) "Combined taskbar button failed a foreground window joining its group"
            onGroup combined (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            pumpUntil "Taskbar proxies outlived their combined taskbar button" (fun () -> not(TaskbarProxies.contains proxy.Head))

            // Restart discovers the same HWNDs; reclaim buttons without showing or activating them.
            let restarted = desktopApi.createGroup(false) :?> GroupInfo
            let reclaimed = Collections.Generic.List<IntPtr>()
            onGroup restarted (fun _ -> (TaskbarRestorePlugin(reclaimed.Add) :> IPlugin).init())
            let wasVisible = [a;b] |> List.map WinUserApi.IsWindowVisible
            for hwnd in [a;b] do (restarted :> IGroup).addWindow(hwnd,false)
            pumpUntil "Restart grouping did not acquire its windows" (fun () -> nativeOrder restarted=[a;b])
            check (onGroup restarted (fun _ -> reclaimed |> Seq.toList) = [a;b]) "Restart did not restore ordinary taskbar buttons"
            check ([a;b] |> List.map WinUserApi.IsWindowVisible = wasVisible) "Taskbar recovery changed application visibility"
            (restarted :> IGroup).destroy()
            pumpUntil "Group exit did not restore remaining taskbar buttons" (fun () -> restarted.isExited && reclaimed.Count=4)
            check (reclaimed |> Seq.skip 2 |> Set.ofSeq = Set.ofList [a;b]) "Group exit restored unrelated taskbar windows"
            check (WinUserApi.IsWindow a && WinUserApi.IsWindow b) "Taskbar recovery closed application windows"

            // Menu choices survive full group teardown while the foreign windows stay open.
            let remembered = desktopApi.createGroup(false) :?> GroupInfo
            (remembered :> IGroup).addWindow(a,false)
            pumpUntil "Remembered group did not acquire its window" (fun () -> nativeOrder remembered=[a])
            let rememberedLayout = {dock=TabDockRight;columnWidth=308;normalAlignment=TabRight;insideAlignment=TabLeft}
            onGroup remembered (fun group -> group.applyTabLayout rememberedLayout)
            check (WindowTabLayout.read a=Some rememberedLayout) "Menu layout was not saved immediately on its window"
            onGroup remembered (fun group -> group.removeWindow a)
            (remembered :> IGroup).destroy()
            pumpUntil "Remembered group did not exit" (fun () -> remembered.isExited)
            api.setValue("tabPosition",box "Left")
            api.setValue("tabColumnWidth",box 180)
            let reopened = desktopApi.createGroup(false) :?> GroupInfo
            (reopened :> IGroup).addWindow(a,false)
            pumpUntil "Restarted group did not acquire its window" (fun () -> nativeOrder reopened=[a])
            check (onGroup reopened (fun group -> group.tabLayout)=rememberedLayout)
                  "Restarted group replaced menu choices with global defaults"
            let explicitLayout = {rememberedLayout with dock=TabDockLeft;columnWidth=264}
            onGroup reopened (fun group -> group.applyTabLayout explicitLayout)
            check (WindowTabLayout.read a=Some explicitLayout) "Updated menu choice left stale window metadata"
            onGroup reopened (fun group -> group.removeWindow a)
            let explicit = desktopApi.createGroup(false) :?> GroupInfo
            onGroup explicit (fun group -> group.applyTabLayout rememberedLayout)
            (explicit :> IGroup).addWindow(a,false)
            pumpUntil "Explicit group did not acquire its window" (fun () -> nativeOrder explicit=[a])
            check (onGroup explicit (fun group -> group.tabLayout)=rememberedLayout)
                  "Stale window metadata overrode an explicit restored layout"
            onGroup explicit (fun group -> group.removeWindow a)
            use unrelated = new Form(Text="Unrelated",ShowInTaskbar=false)
            check ((WindowTabLayout.read unrelated.Handle).IsNone) "New application window inherited another window's layout"
            check ((WindowTabLayout.read c).IsNone) "Closed HWND retained another window's layout"
            api.setValue("tabPosition",box "Horizontal")
            api.setValue("tabColumnWidth",box 220)

            // Save and restore the production workspace across group STAs, after defaults change.
            let left = desktopApi.createGroup(false) :?> GroupInfo
            let right = desktopApi.createGroup(false) :?> GroupInfo
            let leftLayout = {dock=TabDockLeft;columnWidth=284;normalAlignment=TabRight;insideAlignment=TabCenter}
            let rightLayout = {dock=TabDockRight;columnWidth=316;normalAlignment=TabLeft;insideAlignment=TabRight}
            for info,hwnd,layout in [left,a,leftLayout;right,b,rightLayout] do
                onGroup info (fun group -> group.applyTabLayout layout)
                (info :> IGroup).addWindow(hwnd,false)
            pumpUntil "Workspace groups did not acquire windows" (fun () -> nativeOrder left=[a] && nativeOrder right=[b])
            workspaceWindows <- List2([a;b])
            let alerts = Collections.Generic.List<AlertKind>()
            Alert.install(fun kind _ _ -> alerts.Add kind)
            let model = WorkspaceModel()
            model.init()
            model.create()
            model.create()
            pumpUntil "Workspace save did not collect group layouts" (fun () -> model.workspaces.length=1)
            let saved = model.workspaces.head
            check (saved.groups.list |> List.map(fun group -> group.cast<WorkspaceGroup>().tabLayout) |> Set.ofList = Set.ofList [Some leftLayout;Some rightLayout])
                  "Workspace snapshot lost different groups' layouts"
            api.setValue("tabPosition",box "Horizontal")
            api.setValue("tabColumnWidth",box 220)
            api.setValue("alignment",box "Left")
            let reloaded = WorkspaceModel()
            reloaded.init()
            reloaded.selected <- reloaded.workspaces.head
            reloaded.restore()
            let restoredGroup hwnd =
                match desktopApi.groups.list |> List.filter(fun group -> group.windows.contains((=)hwnd)) with
                | [group] -> Some(group :?> GroupInfo)
                | _ -> None
            pumpUntil "Workspace restore did not transfer both windows" (fun () ->
                restoredGroup a |> Option.exists(fun info -> info.hwnd<>left.hwnd) &&
                restoredGroup b |> Option.exists(fun info -> info.hwnd<>right.hwnd))
            for hwnd,layout in [a,leftLayout;b,rightLayout] do
                check (onGroup (restoredGroup hwnd).Value (fun group -> group.tabLayout)=layout)
                      "Workspace restore used new defaults instead of the saved tab layout"
            check (alerts |> Seq.forall((=) AlertKind.Info)) "Workspace layout restore reported a warning"
            for info in desktopApi.groups.list |> List.map(fun group -> group :?> GroupInfo) do
                if not info.isExited then onGroup info (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            workspaceWindows <- List2()
            let closing = desktopApi.createGroup(false) :?> GroupInfo
            let mutable captured : GroupTabLayout option option = None
            let mutable captures = 0
            (closing :> IGroup).destroy()
            closing.captureTabLayout(fun value -> captures <- captures+1;captured <- Some value)
            pumpUntil "Layout snapshot hung behind group exit" (fun () -> closing.isExited && captured.IsSome)
            check (captured=Some None && captures=1) "Closed group returned stale or duplicate layout snapshots"

            // Neovide and Alacritty windows join, leave and join again as they start. Explorer can
            // register the returning preview after the order that places it and show it first, so a
            // changed order is sent again once settled.
            do
                let tab text : string * TbButtonTab =
                    text, { icon=Drawing.SystemIcons.Application; text=text; activate=ignore; close=ignore; preview=fun _ -> None }
                let info texts : TbButton =
                    { icon=Drawing.SystemIcons.Application; text="Order"; bounds=Rect(Pt(-20000,-20000),Sz(320,200))
                      activate=ignore; toggleMinimizeRestore=ignore; close=ignore; tabs=List2(texts |> List.map tab) }
                let button = new TaskBarButton(info ["a";"b"])
                try
                    pumpUntil "First preview order was not sent again" (fun () -> not button.isResendingTabOrder)
                    button.update(info ["a";"b";"c"])
                    button.update(info ["a";"b"])
                    button.update(info ["a";"b";"c"])
                    check button.isResendingTabOrder "A preview that rejoined is not ordered again"
                    pumpUntil "Preview order was not sent again" (fun () -> not button.isResendingTabOrder)
                    button.update(info ["a";"b";"c"])
                    check (not button.isResendingTabOrder) "An unchanged order is sent again"
                    // A preview that leaves frees its window, class and procedure then, not with the group.
                    let leaving = (button.tryTabWindow "c").Value.hwnd
                    for _ in 1..20 do
                        button.update(info ["a";"b"])
                        button.update(info ["a";"b";"c"])
                    check (button.windowCount = 4) (sprintf "Previews that left kept %d windows" (button.windowCount-4))
                    check (not(WinUserApi.IsWindow leaving) && not(TaskbarProxies.contains leaving)) "A preview that left kept its window"
                finally (button :> IDisposable).Dispose()

            // The setting rebuilds open groups that follow it, keeping what their tab menu chose.
            api.setValue("combineIconsInTaskbar", box false)
            let groupsOf hwnd = desktopApi.groups.list |> List.filter(fun g -> g.windows.contains((=)hwnd)) |> List.map(fun g -> g :?> GroupInfo)
            let soleGroup hwnd combinedIcons =
                match groupsOf hwnd with
                | [g] when g.isSuperBarEnabled=combinedIcons && not g.isRetiring -> Some g
                | _ -> None
            let following = desktopApi.createGroup(false) :?> GroupInfo
            let chosen = desktopApi.createGroup(false) :?> GroupInfo
            (following :> IGroup).addWindow(a,false)
            (chosen :> IGroup).addWindow(b,false)
            pumpUntil "Groups did not acquire their windows" (fun () -> nativeOrder following = [a] && nativeOrder chosen = [b])
            let choices = { iconOnly=true; dock=TabDockLeft; columnWidth=280; alignments=[TabUp,TabRight]; autoHideMode=Some "Always" }
            let savedChoices = onGroup following (fun group -> group.applyMenuChoices choices; group.menuChoices)
            desktopApi.restartGroup(chosen.hwnd, true)
            pumpUntil "Tab menu did not combine its group's taskbar icons" (fun () -> (soleGroup b true).IsSome)
            let chosenNow = (soleGroup b true).Value
            api.setValue("combineIconsInTaskbar", box true)
            pumpUntil "Setting did not combine an open group's taskbar icons" (fun () -> (soleGroup a true).IsSome)
            let rebuilt = (soleGroup a true).Value
            check (nativeOrder rebuilt = [a]) "Rebuilt group lost its window"
            check (onGroup rebuilt (fun group -> group.menuChoices) = savedChoices) "Rebuilt group lost its tab menu choices"
            api.setValue("combineIconsInTaskbar", box false)
            pumpUntil "Setting did not separate an open group's taskbar icons" (fun () -> (soleGroup a false).IsSome)
            check (soleGroup b true = Some chosenNow) "Setting overrode a group's taskbar choice from its tab menu"
            // Turned back before the rebuild runs, the group stays as it is.
            let separate = (soleGroup a false).Value
            api.setValue("combineIconsInTaskbar", box true)
            api.setValue("combineIconsInTaskbar", box false)
            pumpUntil "Group stayed retiring after the setting was turned back" (fun () -> not separate.isRetiring)
            check (soleGroup a false = Some separate) "Setting turned back still rebuilt the group"
            for info in [separate;chosenNow] do
                onGroup info (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
            // Exercise production drag targets on their owning STAs without taking over the mouse.
            for sourceDock,targetDock in [TabDockTop,TabDockLeft;TabDockLeft,TabDockTop;
                                          TabDockTop,TabDockRight;TabDockRight,TabDockTop;
                                          TabDockLeft,TabDockRight;TabDockRight,TabDockLeft] do
                let dragSource = desktopApi.createGroup(false) :?> GroupInfo
                let dragTarget = desktopApi.createGroup(false) :?> GroupInfo
                for hwnd in [a;b] do (dragSource :> IGroup).addWindow(hwnd,false)
                (dragTarget :> IGroup).addWindow(d,false)
                pumpUntil "Drag groups did not acquire windows" (fun () -> nativeOrder dragSource=[a;b] && nativeOrder dragTarget=[d])
                let data = onGroup dragSource (fun group ->
                    group.ts.dock <- sourceDock
                    let size = if group.ts.isVertical then Sz(220,400) else Sz(600,40)
                    group.ts.setPlacement({bounds=Rect(Pt(-20000,-20000),size);showInside=false})
                    let tab = Tab b
                    let _,sprite = group.ts.tabSprites.pick(fun (location,sprite) -> if sprite.id=tab then Some(location,sprite) else None)
                    let data = {tab=tab;tabSize=sprite.size;tabOffset=Pt(sprite.size.width/2,sprite.size.height/2);tabInfo=group.ts.tabInfo tab}
                    let proportional = {data with tabSize=Sz(200,32);tabOffset=Pt(100,16)}
                    check (proportional.offsetIn(Sz(300,48))=Pt(150,24)) "Cross-DPI drag lost its grabbed point"
                    check (proportional.offsetIn(Sz(120,64))=Pt(60,32)) "Cross-orientation drag lost its grabbed point"
                    let target = dragTargetFor group.ts.hwnd
                    check (target.dragEnter (box data) (group.ts.tabLocation(tab).add(data.tabOffset))) "Source rejected a drag"
                    target.dragExit()
                    data)
                pumpUntil "Drag source retained moved window" (fun () -> nativeOrder dragSource=[a] && publicOrder dragSource=[a])
                onGroup dragTarget (fun group ->
                    group.ts.dock <- targetDock
                    let size = if group.ts.isVertical then Sz(220,400) else Sz(600,40)
                    group.ts.setPlacement({bounds=Rect(Pt(-20000,-20000),size);showInside=false})
                    let target = dragTargetFor group.ts.hwnd
                    check (target.dragEnter (box data) (Pt(1,1))) "Destination rejected a drag"
                    target.dragEnd())
                pumpUntil "Cross-orientation drag lost its insertion order" (fun () -> nativeOrder dragTarget=[b;d] && publicOrder dragTarget=[b;d])
                check (onGroup dragTarget (fun group -> group.ts.dock)=targetDock) "Drag changed the destination orientation"
                for info in [dragSource;dragTarget] do
                    onGroup info (fun group -> for hwnd in group.windows.items.list do group.removeWindow hwnd)
                    (info :> IGroup).destroy()
                pumpUntil "Drag groups did not exit" (fun () -> dragSource.isExited && dragTarget.isExited)
            printfn "PASS: real HWND grouping, duplicate add, reorder, cross-STA transfer, six cross-orientation drags, destroyed-window removal, ungrouping, taskbar recovery and taskbar icon rebuilds."
        finally
            let rest = desktopApi.groups.list |> List.map(fun g -> g :?> GroupInfo)
            for info in List.distinct([source;target;combined] @ rest) do
                if not info.isExited then
                    onGroup info (fun group ->
                        for hwnd in group.windows.items.list do group.removeWindow hwnd)
                    (info :> IGroup).destroy()
            pumpUntil "Groups remained registered after teardown" (fun () -> desktop.retainedGroupCount = 0)
            for form in forms do form.Dispose()
    finally
        Environment.CurrentDirectory <- previousDirectory
TestInit.run main
