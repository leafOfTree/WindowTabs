// Native mouse/placement regressions on off-screen HWNDs and isolated settings.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open System.IO
open System.Reflection
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let frameProperty = typeof<TabStrip>.GetProperty("renderCount",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public)
let frames strip = frameProperty.GetValue(strip) :?> int64
let main() =
    let original = Environment.CurrentDirectory
    let originalDpi = Dpi.value()
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","interaction-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true,saveDelay=0)
        let api = settings :> ISettings
        check (SettingsCatalog.shortcutDefault "numberLeader"=0x0453) "Tab selection must default to Alt+S"
        check (Theme.leastUsedColor [0;1;2;0;3]=4) "By-window allocation did not balance colours"
        check (Theme.tabColorOrder.Head=1 && List.last Theme.tabColorOrder=0 && (Theme.tabColorOrder |> List.sort)=[0..15]) "Colour menu must start with blue, end with grey and retain every stored index"
        check (Theme.leastUsedColor []=1) "The first window must use blue rather than grey"
        check (Theme.leastUsedColor [1..7]=8) "The eighth window did not move on to the extra colours"
        check (Theme.leastUsedColor [1..15]=0) "Grey must only be used after every coloured choice"
        check (Theme.leastUsedColor [0..15]=1) "The seventeenth window did not start the palette again"
        check ([for c in 'a'..'z' -> Theme.appColorIndex (string c + ".exe")] |> List.forall(fun index -> index>=0 && index<Theme.tabPaletteSize)) "App colour fell outside the palette"
        check (([for c in 'a'..'z' -> Theme.appColorIndex (string c + ".exe")] |> List.distinct).Length>8) "App colours did not use the second eight"
        check (Theme.appColorIndex "Editor.exe"=Theme.appColorIndex "EDITOR.EXE") "App colour hash changed with case"
        let colors = WindowTabColors()
        let peersAsked = ref 0
        let resolve hwnd peers remembered = colors.resolve hwnd (fun () -> peersAsked.Value <- peersAsked.Value+1; peers) "editor.exe" "ByWindow" remembered
        let a,b,c = IntPtr(1),IntPtr(2),IntPtr(3)
        let first = resolve a [a;b] Map.empty
        check (resolve b [a;b] Map.empty<>first) "By-window colours repeated a used colour too soon"
        let red = CustomColor Color.Red
        colors.setOverride a (Some red)
        check (resolve a [a;c] Map.empty=Some red) "Window colour was lost across groups"
        colors.setOverride a None
        check (resolve a [a;c] Map.empty=first) "Clearing custom colour changed its automatic assignment"
        check (peersAsked.Value=2) "Peers were listed again after the window had its colour"
        let remembered = Map.ofList [@"C:\Apps\EDITOR.EXE","#00FF00"]
        let resolveAt path hwnd = colors.resolve hwnd (fun () -> [hwnd]) path "ByWindow" remembered
        check (resolveAt @"c:\apps\editor.exe" a=Some(CustomColor(Color.FromArgb(0,255,0)))) "Remembered colour did not override automatic colour or depended on path case"
        check (resolveAt @"C:\Other\EDITOR.EXE" a=first) "Remembered colour applied to another app with the same name"
        colors.setOverride a (Some red)
        check (resolveAt @"C:\Apps\EDITOR.EXE" a=Some red) "Remembered colour overrode window colour"
        let loads = ref 0
        let load() = loads.Value <- loads.Value+1; @"C:\Apps\Editor.exe"
        for _ in 1..3 do colors.path a load |> ignore
        check (loads.Value=1) "Process path was looked up again for the same window"
        colors.remove a
        check (colors.getOverride a=None) "Destroyed HWND retained custom colour"
        colors.path a load |> ignore
        check (loads.Value=2) "Destroyed HWND kept its process path"
        use swatch = new Bitmap(16,16)
        use ink = Graphics.FromImage(swatch)
        ink.Clear(Color.Red)
        let item = CmiRegular({text="Colour";image=Some(Img(swatch));flags=List2();click=ignore})
        let menus = List2([CmiPopUp({text="Colours";image=None;items=List2([item])})])
        let before,_,_,_ = RuntimeDiagnostics.resourceCounts()
        for _ in 1..100 do
            use menu = new NativeContextMenu(menus)
            check (menu.handle<>IntPtr.Zero) "Native colour menu was not created"
        let after,_,_,_ = RuntimeDiagnostics.resourceCounts()
        check (after-before<5) "Native colour menus leaked GDI bitmaps"
        // A swatch replaces the check mark, so the checked one must look different.
        for colour in [Color.Red;Color.Yellow;Color.Black;Color.White] do
            ink.Clear(colour)
            use marked = MenuImages.checkedCopy swatch
            let changed = seq { for x in 0..15 do for y in 0..15 do if marked.GetPixel(x,y).ToArgb()<>colour.ToArgb() then yield () } |> Seq.length
            check (changed>8) (sprintf "Checked %A swatch has no visible mark" colour)
            check (swatch.GetPixel(8,8).ToArgb()=colour.ToArgb()) "Marking a checked swatch changed the caller's image"
        let checkedItem = CmiRegular({text="Colour";image=Some(Img(swatch));flags=List2([MenuFlags.MF_CHECKED]);click=ignore})
        let before,_,_,_ = RuntimeDiagnostics.resourceCounts()
        for _ in 1..100 do
            use menu = new NativeContextMenu(List2([checkedItem]))
            check (menu.handle<>IntPtr.Zero) "Native checked colour menu was not created"
        let after,_,_,_ = RuntimeDiagnostics.resourceCounts()
        check (after-before<5) "Checked colour menus leaked GDI bitmaps"
        let leader = NumberLeaderState()
        let now = DateTime.UtcNow
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0x32 3 "123456789" = (true,Some 1) && not leader.active) "Leader digit did not select and disarm"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0x1B 3 "123456789" = (true,None) && not leader.active) "Escape did not cancel and consume"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0x41 3 "123456789" = (false,None) && not leader.active) "Other key must cancel and pass through"
        leader.arm (IntPtr(1)) now
        check (leader.validate (IntPtr(1)) (now.AddSeconds(9.99))) "Leader expired before the ten-second selection window"
        check (not (leader.validate (IntPtr(1)) (now.AddSeconds(10.0)))) "Leader did not time out"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) (now.AddSeconds(9.0)) 0x32 3 "123456789"=(true,Some 1)) "A selection late in the ten-second window did not activate"
        leader.arm (IntPtr(1)) now
        check (not (leader.validate (IntPtr(2)) now)) "Leader survived foreground change"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0x39 3 "123456789" = (true,None)) "Missing leader digit must be consumed without activation"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0x62 3 "123456789" = (true,Some 1) && not leader.active) "Leader ignored a number-pad digit"
        // Switching focus disarms the leader, but the captured key stays owned through release.
        for vk in [0x41;0xBA;0x1B] do
            let held = NumericShortcutCapture()
            leader.arm (IntPtr(1)) now
            let consumed,_ = leader.key (IntPtr(1)) now vk 10 "ASDFGHJKL;"
            check (consumed && held.handle(WindowMessages.WM_KEYDOWN,vk,Some 0)=(true,Some 0)) "Leader selection press leaked"
            for _ in 1..5 do
                let repeat,_ = leader.key (IntPtr(2)) now vk 10 "ASDFGHJKL;"
                check (not repeat && held.handle(WindowMessages.WM_KEYDOWN,vk,None)=(true,None)) "Held leader key leaked or activated twice after switching focus"
            check (held.handle(WindowMessages.WM_KEYUP,vk,None)=(true,None)) "Leader selection release leaked"
            check (held.handle(WindowMessages.WM_KEYDOWN,vk,None)=(false,None)) "A fresh press remained captured after release"
        check (NumberLeaderKeys.tryNormalize "asdfghjkl;"=Some "ASDFGHJKL;") "Home-row keys did not normalize"
        for value in ["";"AA";"aA";"A A";"中文";"!"] do
            check ((NumberLeaderKeys.tryNormalize value).IsNone) "Invalid or duplicate selection keys were accepted"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0xBA 10 "ASDFGHJKL;"=(true,Some 9)) "Home-row semicolon did not select the tenth tab"
        leader.arm (IntPtr(1)) now
        check (leader.key (IntPtr(1)) now 0x5A 26 "ABCDEFGHIJKLMNOPQRSTUVWXYZ"=(true,Some 25)) "Letter keys did not support tabs beyond nine"
        api.setValue("numberLeaderKeys",box "asdfghjkl;")
        check (api.getValue("numberLeaderKeys")=box "ASDFGHJKL;" && string api.root.["numberLeaderKeys"]="ASDFGHJKL;") "Selection keys failed to normalize and persist"
        let savedKeys = api.root.DeepClone() :?> Newtonsoft.Json.Linq.JObject
        api.root <- savedKeys
        check (api.getValue("numberLeaderKeys")=box "ASDFGHJKL;") "Custom selection keys failed to round trip"
        api.root <- SettingsCatalog.resetRoot savedKeys false false
        check (api.getValue("numberLeaderKeys")=box "123456789") "Settings reset did not restore numeric selection keys"
        api.setValue("numberLeaderKeys",box "AA")
        check (api.getValue("numberLeaderKeys")=box "123456789") "Invalid persisted selection keys did not use the default"
        for dpi in [96;144] do
            Dpi.set dpi
            api.setValue("enableHoverActivate",box false)
            let clicks = ResizeArray<MouseButton * Tab * TabPart * MouseAction * Pt>()
            let closes,activations = ResizeArray<Tab>(),ResizeArray<Tab>()
            let monitor = { new ITabStripMonitor with
                                member _.tabClick args = clicks.Add(args)
                                member _.tabActivate tab = activations.Add(tab)
                                member _.tabClose tab = closes.Add(tab)
                                member _.tabMoved(_,_) = ()
                                member _.windowMsg _ = () }
            let strip = new TabStrip(monitor)
            try
                strip.setTabAppearance(ThemeService.currentAppearance().scaled)
                strip.setDefaultAlignment "Left"
                for id in 1..5 do strip.addTab(Tab(IntPtr(100+id)))
                let size = Dpi.scaleSize(Sz(1000,28))
                let placement point size inside = {bounds=Rect(point,size);showInside=inside}
                let start = Pt(-20000,-20000)
                strip.setPlacement(placement start size false)
                strip.visible <- true
                let selectionFrames = frames strip
                let order = strip.zorder
                strip.setTabBgColor(order.head,None)
                strip.zorder <- List2(order.list)
                check (frames strip=selectionFrames) "Repeated selection or clearing absent attention repainted"
                let attention = ThemeService.currentAppearance().scaled.tabFlashBgColor
                strip.setTabBgColor(order.head,Some attention)
                check (frames strip=selectionFrames+1L) "Attention did not render"
                strip.setTabBgColor(order.head,Some attention)
                check (frames strip=selectionFrames+1L) "Repeated attention repainted"
                strip.setTabBgColor(order.head,None)
                check (frames strip=selectionFrames+2L) "Clearing existing attention did not render"
                let initialFrames = frames strip
                for _ in 1..20 do strip.isShrunk <- false
                check (frames strip=initialFrames) "Repeated expanded state repainted"
                let send message (point:Pt) =
                    WinUserApi.SendMessage(strip.hwnd,message,IntPtr.Zero,IntPtr((point.y <<< 16) ||| point.x)) |> ignore
                let hover x = send WindowMessages.WM_MOUSEMOVE (Pt(Dpi.scale x,Dpi.scale 14))
                let count = frames strip
                hover 60
                check (frames strip=count+1L && strip.isMouseOver.value) "Entering a tab did not render exactly once"
                let count = frames strip
                for x in 61..90 do hover x
                check (frames strip=count) "Moving inside one tab repainted unchanged pixels"
                hover 260
                check (frames strip=count+1L) "Crossing a tab did not render exactly once"
                api.setValue("enableHoverActivate",box true)
                let count = frames strip
                for _ in 1..5 do hover 260
                check (activations.Count=5 && frames strip=count) "Unchanged hover suppressed activation or repainted"
                api.setValue("enableHoverActivate",box false)

                // Click/drag-start callbacks retain button, part, action and pointer.
                let background = Pt(Dpi.scale 60,Dpi.scale 14)
                send WindowMessages.WM_LBUTTONDOWN background
                send WindowMessages.WM_LBUTTONUP background
                check (clicks.Count=2) "Background click callbacks were lost"
                let button,_,part,action,point = clicks.[0]
                check (button=MouseLeft && part=TabBackground && action=MouseDown && point=background) "Drag-start callback changed"

                hover 60
                let tabPosition,tabSprite = strip.sprite.children.head
                let closePosition,closeSprite = tabSprite.children.list |> List.find(fun (_,sprite) -> sprite :? CloseButtonSprite)
                let close = closeSprite :?> CloseButtonSprite
                let closePoint = tabPosition.add(closePosition).add(Pt(close.size.width/2,close.size.height/2))
                send WindowMessages.WM_MOUSEMOVE closePoint
                let count = frames strip
                send WindowMessages.WM_LBUTTONDOWN closePoint
                check (frames strip=count+1L) "Close press rendered more than once"
                send WindowMessages.WM_LBUTTONUP closePoint
                check (closes.Count=1 && frames strip=count+2L) "Close release did not close once and render once"
                send WindowMessages.WM_LBUTTONDOWN closePoint
                send WindowMessages.WM_LBUTTONUP background
                check (closes.Count=1) "Releasing outside the close button closed a tab"
                send WindowMessages.WM_MOUSELEAVE Pt.empty
                check (not strip.isMouseOver.value) "Mouse leave retained hover"
                let count = frames strip
                send WindowMessages.WM_MOUSELEAVE Pt.empty
                check (frames strip=count) "Repeated mouse leave repainted"

                let os = OS()
                let shadow = os.windowsInZorder.list |> List.find(fun window -> window.parent.hwnd=strip.hwnd)
                let count = frames strip
                for x in 1..20 do
                    strip.setPlacement(placement (start.add(Pt(x*3,x*2))) size false)
                    strip.visible <- true
                check (frames strip=count) "Pure moves or repeated visibility regenerated pixels"
                let moved = start.add(Pt(60,40))
                check (strip.bounds.location=moved && strip.bounds.size=size) "Tab strip did not follow the move"
                check (shadow.location=moved.sub(Pt(Dpi.scale 15,Dpi.scale 15)) && shadow.isVisible) "Shadow did not follow the move"
                check (os.windowFromHwnd(strip.hwnd).prevZorder.hwnd=shadow.hwnd) "Moving changed shadow stacking"
                let larger = Sz(size.width+Dpi.scale 40,size.height)
                strip.setPlacement(placement moved larger false)
                check (frames strip=count+1L && strip.bounds.size=larger) "Resize did not repaint once"
                strip.setPlacement(placement moved larger true)
                check (frames strip=count+2L && shadow.location.y=moved.y) "Direction change did not repaint or reposition shadow"
                strip.setPlacement(placement moved larger false)
                strip.isShrunk <- true
                let count = frames strip
                let next = moved.add(Pt(30,30))
                strip.setPlacement(placement next larger false)
                check (frames strip=count && strip.bounds.location.y=next.y+larger.height-Dpi.scale 4) "Collapsed strip lost its movement offset"
                check (not shadow.isVisible) "Collapsed strip showed a shadow"
                strip.isShrunk <- false
                strip.visible <- false
                let count = frames strip
                strip.setPlacement(placement start larger false)
                check (frames strip=count && not shadow.isVisible) "Hidden movement painted or showed a shadow"
                strip.visible <- true
                check (strip.bounds.location=start && shadow.isVisible) "Show did not use the latest hidden placement"
                os.windowFromHwnd(strip.hwnd).hide()
                strip.visible <- true
                check (os.windowFromHwnd(strip.hwnd).isVisible && shadow.isVisible) "Equal visibility did not recover an externally hidden strip"
                // External activation still recovers a hidden owned shadow.
                shadow.hide()
                strip.refreshShadow()
                Application.DoEvents()
                check shadow.isVisible "Deferred activation refresh stopped restoring the shadow"
            finally strip.destroy()
        printfn "PASS: unchanged hover/moves skip rendering; cross-tab hover, clicks, capture, close, resize, direction, collapse and shadow recovery."
    finally
        Dpi.set originalDpi
        Environment.CurrentDirectory <- original
TestInit.run main
