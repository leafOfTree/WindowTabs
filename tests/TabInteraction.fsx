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
open System.Runtime.InteropServices

[<Struct; StructLayout(LayoutKind.Sequential)>]
type NativeBitmap =
    val mutable kind:int
    val mutable width:int
    val mutable height:int
    val mutable stride:int
    val mutable planes:uint16
    val mutable depth:uint16
    val mutable bits:IntPtr

module NativeBitmapApi =
    [<DllImport("gdi32.dll",EntryPoint="GetObjectW")>]
    extern int GetObject(IntPtr bitmap,int size,NativeBitmap& info)

let check condition message = if not condition then failwith message
let frameProperty = typeof<TabStrip>.GetProperty("renderCount",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public)
let frames strip = frameProperty.GetValue(strip) :?> int64
let main() =
    // Frame counts below assume a change paints once; the auto-hide animation is tested on its own.
    TabReveal.forced <- Some false
    let original = Environment.CurrentDirectory
    let originalDpi = Dpi.value()
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","interaction-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true,saveDelay=0)
        let api = settings :> ISettings
        for bar in [Theme.light.tabNormalBgColor;Theme.dark.tabNormalBgColor] do
            use source = new Bitmap(4,1)
            for x,color in [0,Color.Red;1,Color.White;2,Color.FromArgb(128,20,40,60);3,Color.Transparent] do source.SetPixel(x,0,color)
            use softened = TabDimming.render bar source
            for x in 0..3 do
                check (softened.GetPixel(x,0).A=source.GetPixel(x,0).A) "Whole-strip dimming changed opacity or rounded-edge transparency"
        // A frame part way keeps each row where it sits in the full strip, from the window edge out.
        do
            use full = new Bitmap(8,10)
            use bar = new Bitmap(8,2)
            using (Graphics.FromImage(full)) (fun g -> g.Clear(Color.Red))
            using (Graphics.FromImage(bar)) (fun g -> g.Clear(Color.Blue))
            for direction in [TabUp;TabDown] do
                for reveal,height in [0.0,2;0.5,6;1.0,10] do
                    let image,offset = TabReveal.frame full bar reveal direction
                    try
                        check (image.height=height && offset=(if direction=TabUp then 10-height else 0))
                              (sprintf "Auto-hide frame at %.1f is %d tall at %d" reveal image.height offset)
                    finally image.bitmap.Dispose()
        let savedAppearance = api.appearance
        for mode,name in [LightTheme,"light";DarkTheme,"dark"] do
            api.updateAppearance(fun value -> {value with mode=mode})
            let mutable selected = 0
            let mutable closed = false
            use dot = MenuImages.colorDot 16 Color.CornflowerBlue
            let command flags image text = CmiRegular({text=text;image=image;flags=List2(flags);click=fun () -> selected <- selected+1})
            let entries = List2([command [] None "Open new tab\tCtrl+Alt+N";
                                 command [MenuFlags.MF_CHECKED] None "Checked";
                                 CmiSeparator;
                                 CmiPopUp({text="Tab color";image=None;items=List2([command [MenuFlags.MF_CHECKED] (Some(Img(dot))) "Blue";command [MenuFlags.MF_DISABLED] None "Disabled"])})])
            use menu = new ThemedContextMenu(entries,fun () -> closed <- true)
            let first = menu.Items.[0] :?> ToolStripMenuItem
            let nested = menu.Items.[3] :?> ToolStripMenuItem
            let colour = nested.DropDownItems.[0] :?> ToolStripMenuItem
            check (first.ShortcutKeyDisplayString="Ctrl+Alt+N" && first.Text="Open new tab") "Themed menu lost its shortcut column"
            check (colour.Checked && not nested.DropDownItems.[1].Enabled) "Themed submenu lost checked or disabled state"
            check (not(Object.ReferenceEquals(colour.Image,dot))) "Themed menu retained a caller-owned image"
            dot.Dispose()
            menu.Show(Point(40,40))
            Application.DoEvents()
            // Pointing at an item with a submenu opens it after a short pause of our own, not the
            // Windows menu delay through WinForms' timer, which a menu of an app behind could lose.
            // The pointer entering an item, as WinForms reports it: HandleMouseEnter raises MouseEnter.
            let enter = typeof<ToolStripItem>.GetMethod("HandleMouseEnter",BindingFlags.Instance ||| BindingFlags.NonPublic)
            let centre (item:ToolStripItem) = item.Owner.RectangleToScreen(item.Bounds) |> fun r -> Point(r.X+r.Width/2,r.Y+r.Height/2)
            enter.Invoke(nested,[|box EventArgs.Empty|]) |> ignore
            menu.openHovered(Point(-30000,-30000))
            check (not nested.DropDown.Visible) "A submenu opened with the pointer no longer on its item"
            menu.openHovered(centre nested)
            Application.DoEvents()
            check nested.DropDown.Visible "Pointing at an item with a submenu did not open it"
            enter.Invoke(first,[|box EventArgs.Empty|]) |> ignore
            menu.openHovered(centre first)
            Application.DoEvents()
            check (not nested.DropDown.Visible) "Pointing at another item left a neighbour's submenu open"
            // Pointing around must not give plain items a submenu of their own: a click on one then
            // opened that empty submenu instead of running its command.
            let dropDownField = typeof<ToolStripDropDownItem>.GetField("dropDown",BindingFlags.Instance ||| BindingFlags.NonPublic)
            check (menu.Items |> Seq.cast<ToolStripItem> |> Seq.forall(fun item ->
                       match item with
                       | :? ToolStripMenuItem as entry when not entry.HasDropDownItems -> isNull (dropDownField.GetValue(entry))
                       | _ -> true))
                  "Pointing at menu items gave a plain item an empty submenu"
            nested.ShowDropDown()
            Application.DoEvents()
            menu.dismissOutside(nested.DropDown.PointToScreen(Point(10,10)))
            check (menu.Visible && nested.DropDown.Visible && not closed) "Clicking inside a submenu dismissed the menu"
            nested.HideDropDown()
            first.PerformClick()
            menu.Close()
            check (closed && selected=0 && not menu.IsDisposed) "Menu command or disposal ran inside the close event"
            let deadline = DateTime.UtcNow.AddSeconds(2.0)
            while not menu.IsDisposed && DateTime.UtcNow<deadline do
                Application.DoEvents()
                Threading.Thread.Sleep(5)
            check (menu.IsDisposed && selected=1) "Themed menu did not retire and dispatch its command once"
            use dismissed = new ThemedContextMenu(List2([command [] None "Cancel test"]),ignore)
            dismissed.Show(Point(40,40))
            Application.DoEvents()
            dismissed.dismissOutside(dismissed.PointToScreen(Point(10,10)))
            check dismissed.Visible "Clicking inside the main menu dismissed it"
            dismissed.dismissOutside(Point(dismissed.Right+100,dismissed.Bottom+100))
            check (not dismissed.Visible && selected=1) "Outside click failed to dismiss the menu or ran a command"
            let deadline = DateTime.UtcNow.AddSeconds(2.0)
            while not dismissed.IsDisposed && DateTime.UtcNow<deadline do
                Application.DoEvents()
                Threading.Thread.Sleep(5)
            check dismissed.IsDisposed "Outside-click dismissal leaked the menu"
            // The click that opens a tab's menu also brings its window forward, and that can land
            // after the menu opened: the menu stays for its own windows, and goes for any other.
            for owned in [true;false] do
                use menu = new ThemedContextMenu(List2([command [] None "Focus test"]),ignore,fun _ -> owned)
                menu.Show(Point(40,40))
                Application.DoEvents()
                menu.Close(ToolStripDropDownCloseReason.AppFocusChange)
                Application.DoEvents()
                check (menu.Visible=owned) (if owned then "The tab's own window coming forward closed its menu" else "Another app taking the focus left the menu open")
                menu.Close(ToolStripDropDownCloseReason.AppClicked)
                Application.DoEvents()
                check (not menu.Visible) "A click outside no longer closes a menu kept through its window's activation"
            // A click on a submenu item closes the whole menu and runs its command at once, though
            // WinForms closes the menu as a focus change before the item's Click records the command.
            do
                let mutable ran = false
                let item = CmiRegular({text="Blue";image=None;flags=List2();click=fun () -> ran <- true})
                use menu = new ThemedContextMenu(List2([CmiPopUp({text="Tab color";image=None;items=List2([item])})]),ignore,fun _ -> true)
                menu.Show(Point(40,40))
                Application.DoEvents()
                let parent = menu.Items.[0] :?> ToolStripMenuItem
                parent.ShowDropDown()
                Application.DoEvents()
                let clicked = typeof<ToolStrip>.GetMethod("OnItemClicked",BindingFlags.Instance ||| BindingFlags.NonPublic)
                clicked.Invoke(parent.DropDown,[|box (ToolStripItemClickedEventArgs(parent.DropDownItems.[0]))|]) |> ignore
                if menu.Visible then menu.Close(ToolStripDropDownCloseReason.AppFocusChange)
                parent.DropDownItems.[0].PerformClick()
                let deadline = DateTime.UtcNow.AddSeconds(1.0)
                while not ran && DateTime.UtcNow<deadline do
                    Application.DoEvents()
                    Threading.Thread.Sleep(5)
                check (not menu.Visible && ran) "A click on a submenu item left the menu open and its command waiting"
            // WinForms may own a menu by a window behind the current one; showing the menu must
            // not raise that window over the current one and its tabs.
            do
                let form() = new Form(ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,
                                      Location=Point(-20000,-20000),Size=Size(120,120))
                use lower = form()
                use upper = form()
                // Shown without activation, like the windows a tab strip's thread sees: none of them
                // is its own active window, which is what left WinForms with a stale owner.
                for window in [lower;upper] do WinUserApi.ShowWindow(window.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
                let flags = SetWindowPosFlags.SWP_NOMOVE ||| SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE
                WinUserApi.SetWindowPos(lower.Handle,upper.Handle,0,0,0,0,flags) |> ignore
                let above (a:IntPtr) (b:IntPtr) =
                    let rec walk (h:IntPtr) = h<>IntPtr.Zero && (h=b || walk (WinUserApi.GetWindow(h,GetWindowConstants.GW_HWNDNEXT)))
                    walk (WinUserApi.GetWindow(a,GetWindowConstants.GW_HWNDNEXT))
                check (above upper.Handle lower.Handle) "Owner test windows are not stacked as set up"
                // The first menu on a thread records the window active then; later ones reused it.
                use first = new ThemedContextMenu(List2([command [] None "First"]),ignore)
                first.Show(lower.Handle,40,40)
                Application.DoEvents()
                first.Close()
                Application.DoEvents()
                WinUserApi.SetWindowPos(lower.Handle,upper.Handle,0,0,0,0,flags) |> ignore
                use menu = new ThemedContextMenu(List2([command [] None "Owner test"]),ignore)
                menu.Show(upper.Handle,40,40)
                Application.DoEvents()
                check (WinUserApi.GetWindow(menu.Handle,GetWindowConstants.GW_OWNER)=upper.Handle) "A tab menu is not owned by the strip it opened from"
                check (above upper.Handle lower.Handle) "Showing a tab menu raised another tab's window over the current window and its tabs"
                menu.Close()
                Application.DoEvents()
        api.updateAppearance(fun _ -> savedAppearance)
        check (SettingsCatalog.shortcutDefault "numberLeader"=0x0453) "Tab selection must default to Alt+S"
        check (Theme.leastUsedColor [0;1;2;0;3]=8) "By-window allocation did not balance colours"
        check (Theme.tabColorOrder.Head=1 && List.last Theme.tabColorOrder=0 && (Theme.tabColorOrder |> List.sort)=[0..15]) "Colour menu must start with blue, end with grey and retain every stored index"
        check ((Theme.tabAllocationOrder |> List.sort)=[0..15] && List.last Theme.tabAllocationOrder=0) "Automatic allocation lost a colour or used grey early"
        for dark in [false;true] do
            let palette = Theme.tabPalette dark
            for a,b in Theme.tabAllocationOrder |> List.filter((<>) 0) |> List.pairwise do
                let distance = abs(palette.[a].GetHue()-palette.[b].GetHue())
                check (min distance (360.0f-distance)>50.0f) "Adjacent automatic colours have similar hues"
        check (Theme.leastUsedColor []=1) "The first window must use blue rather than grey"
        check (Theme.leastUsedColor (Theme.tabAllocationOrder |> List.take 10)=11) "Colour allocation did not move on to the extra colours"
        check (Theme.leastUsedColor [1..15]=0) "Grey must only be used after every coloured choice"
        check (Theme.leastUsedColor [0..15]=1) "The seventeenth window did not start the palette again"
        check ([for c in 'a'..'z' -> Theme.appColorIndex (string c + ".exe")] |> List.forall(fun index -> index>=0 && index<Theme.tabPaletteSize)) "App colour fell outside the palette"
        check (([for c in 'a'..'z' -> Theme.appColorIndex (string c + ".exe")] |> List.distinct).Length>8) "App colours did not use the second eight"
        check (Theme.appColorIndex "Editor.exe"=Theme.appColorIndex "EDITOR.EXE") "App colour hash changed with case"
        check ((Strings.Common.selectedChoice Strings.Settings.tabColorNames.[1]).en="Blue  ✓") "Selected colour is not marked after its name"
        let colors = WindowTabColors()
        let a,b,c = IntPtr(1),IntPtr(2),IntPtr(3)
        let mutable peers = [a;b;c]
        let resolve hwnd remembered = colors.resolve hwnd "editor.exe" "ByWindow" remembered peers
        for hwnd in peers do
            check (colors.resolve hwnd "editor.exe" "Off" Map.empty peers=None) "Disabled automatic colours tint an existing window"
            check (colors.resolve hwnd "editor.exe" "ByApp" Map.empty peers=Some(PaletteColor(Theme.appColorIndex "editor.exe"))) "By-app mode stopped sharing an app's colour"
            check (colors.resolve hwnd "editor.exe" "ByWindow" Map.empty [hwnd]=Some(PaletteColor Theme.tabAllocationOrder.Head)) "Single-window previews did not start with blue"
        let first = resolve a Map.empty
        let existing = [first;resolve b Map.empty;resolve c Map.empty]
        check (existing=(Theme.tabAllocationOrder |> List.take 3 |> List.map(PaletteColor >> Some))) "A group did not start in alternating colour order"
        let other = [IntPtr(50);IntPtr(51);IntPtr(52)]
        let otherColors = other |> List.map(fun hwnd -> colors.resolve hwnd "editor.exe" "ByWindow" Map.empty other)
        check (otherColors=existing) "Another group did not restart the alternating colour order"
        peers <- [c;a;b]
        check ([resolve a Map.empty;resolve b Map.empty;resolve c Map.empty]=existing) "Dragging tabs changed their colours"
        peers <- [a;c]
        colors.remove b
        check (resolve a Map.empty=first && resolve c Map.empty=existing.[2]) "Closing a tab recoloured remaining tabs"
        peers <- [a;c;IntPtr(4)]
        check (resolve (IntPtr(4)) Map.empty=existing.[1]) "A new tab did not use the first available automatic colour"
        let red = CustomColor Color.Red
        colors.setOverride a (Some red)
        check (resolve a Map.empty=Some red) "Window colour was lost across groups"
        colors.setOverride a None
        check (resolve a Map.empty=first) "Clearing custom colour changed its automatic assignment"
        let transferred = colors.resolve c "editor.exe" "ByWindow" Map.empty (other @ [c])
        check (transferred=existing.[2]) "A transferred tab did not keep its colour"
        let palette = WindowTabColors()
        let all = [for id in 1..Theme.tabPaletteSize -> IntPtr(id)]
        let allocated = all |> List.map(fun hwnd -> palette.resolve hwnd "editor.exe" "ByWindow" Map.empty all)
        check ((allocated |> List.distinct).Length=Theme.tabPaletteSize) "Automatic colours repeat before every palette colour is used"
        palette.remove (IntPtr(2))
        let replacement = all |> List.filter((<>) (IntPtr(2))) |> fun remaining -> remaining @ [IntPtr(100)]
        check (palette.resolve (IntPtr(100)) "editor.exe" "ByWindow" Map.empty replacement=allocated.[1]) "Closed windows do not release their automatic colour"
        let remembered = Map.ofList [@"C:\Apps\EDITOR.EXE","#00FF00"]
        let resolveAt path hwnd = colors.resolve hwnd path "ByWindow" remembered peers
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
        for value in ["";"AA";"aA";"A A";"ä¸­æ–‡";"!"] do
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
                let originalFill = (snd strip.tabSprites.head).fillColor
                let renderProperty = typeof<TabStrip>.GetProperty("render",BindingFlags.Instance ||| BindingFlags.NonPublic)
                let pixels() =
                    let image = renderProperty.GetValue(strip) :?> Img
                    try [| for y in 0..image.height-1 do for x in 0..image.width-1 do yield image.bitmap.GetPixel(x,y) |]
                    finally image.bitmap.Dispose()
                let bright = pixels()
                let brightFrames = frames strip
                strip.dimmed <- true
                let dimmedSprite = snd strip.tabSprites.head
                check (frames strip>brightFrames) "Dimming an inactive group did not repaint it"
                if not SystemInformation.HighContrast then
                    let dim = pixels()
                    check (dim.Length=bright.Length && Array.forall2(fun (a:Color) (b:Color) -> a.A=b.A) dim bright) "Inactive group became transparent"
                    check (Array.exists2(fun (a:Color) (b:Color) -> a.ToArgb()<>b.ToArgb()) dim bright) "Inactive group was not dimmed"
                check (dimmedSprite.fillColor.A=255uy) "Inactive group became transparent"
                check (dimmedSprite.fillColor=originalFill) "Group dimming was applied twice instead of to the finished surface"
                strip.dimmed <- false
                check ((snd strip.tabSprites.head).fillColor=originalFill) "Reactivating a group did not restore its colours"
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
                // A menu over the strip ends hover without a move. When it goes with the pointer still
                // over the strip, hover returns without waiting for a move, so auto-hide keeps it open.
                // A pointer given to the strip, so moving the real mouse cannot disturb the checks.
                let pointerProperty = typeof<TabStrip>.GetProperty("pointer",BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public)
                let pointAt (point:Point) = pointerProperty.SetValue(strip,box(fun () -> point))
                pointAt (Point(-30000,-30000))
                strip.refreshHover()
                check (not strip.isMouseOver.value) "Hover returned with the pointer elsewhere"
                let underPointer = Pt(-19000,-19000)
                strip.setPlacement(placement underPointer size false)
                pointAt (Point(underPointer.x+Dpi.scale 60,underPointer.y+Dpi.scale 14))
                Application.DoEvents()
                send WindowMessages.WM_MOUSELEAVE Pt.empty
                strip.refreshHover()
                let restored = strip.isMouseOver.value
                // Pressing a tab hands the capture to the drag check, which Windows reports as a
                // leave: hover holds while the button is down, then follows the pointer.
                use grab = new Form(ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,
                                    Location=Point(-20000,-20000),Size=Size(1,1))
                WinUserApi.ShowWindow(grab.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
                let settle () =
                    let clock = Diagnostics.Stopwatch.StartNew()
                    while clock.ElapsedMilliseconds<150L do
                        Application.DoEvents()
                        Threading.Thread.Sleep(5)
                Application.DoEvents()
                send WindowMessages.WM_MOUSEMOVE (Pt(Dpi.scale 60,Dpi.scale 14))
                WinUserApi.SetCapture(grab.Handle) |> ignore
                send WindowMessages.WM_MOUSELEAVE Pt.empty
                let heldDuringPress = strip.isMouseOver.value
                strip.setPlacement(placement start size false)
                pointAt (Point(-30000,-30000))
                WinUserApi.ReleaseCapture() |> ignore
                settle()
                let droppedAway = not strip.isMouseOver.value
                send WindowMessages.WM_MOUSELEAVE Pt.empty
                Application.DoEvents()
                check restored "The pointer still over the strip after its menu closed did not hover it again"
                check heldDuringPress "Pressing a tab dropped hover while the pointer stayed on it, so auto-hide began to collapse"
                check droppedAway "Hover stayed after a press ended with the pointer away from the strip"
                pointerProperty.SetValue(strip,box(fun () -> Cursor.Position))
                let count = frames strip
                send WindowMessages.WM_MOUSELEAVE Pt.empty
                check (frames strip=count) "Repeated mouse leave repainted"
                // A title cut short shows in full once the pointer rests on its tab; a whole title
                // does not, and a click puts the tip away until the pointer moves to another tab.
                do
                    let titled (id:int) text = strip.setTabInfo(Tab(IntPtr(id)),{ strip.tabInfo(Tab(IntPtr(id))) with text=text })
                    titled 101 (String.replicate 8 "Quarterly report draft ")
                    titled 102 "Notes"
                    titled 103 (String.replicate 8 "Meeting notes and actions ")
                    // Off screen the real pointer is elsewhere, so pumping messages would deliver the
                    // leave TrackMouseEvent reports; the rest is stepped by hand instead.
                    let flags = BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public
                    let shown () = typeof<TabStrip>.GetProperty("titleTipShown",flags).GetValue(strip) :?> bool
                    let waiting () = (typeof<TabStrip>.GetField("titleTimer",flags).GetValue(strip) :?> Timer).Enabled
                    let rest () = typeof<TabStrip>.GetMethod("showTitle",flags).Invoke(strip,[||]) |> ignore
                    hover 60
                    check (waiting() && not (shown())) "A cut-short title did not wait for the pointer to rest"
                    rest()
                    check (shown()) "A cut-short title did not show in full after a rest"
                    hover 460
                    check (shown()) "Moving between cut-short titles did not show the next one at once"
                    hover 260
                    rest()
                    check (not (shown())) "A title that fits showed a tip"
                    hover 60
                    check (waiting()) "Returning to a cut-short title did not wait to show it again"
                    rest()
                    send WindowMessages.WM_LBUTTONDOWN (Pt(Dpi.scale 60,Dpi.scale 14))
                    send WindowMessages.WM_LBUTTONUP (Pt(Dpi.scale 60,Dpi.scale 14))
                    hover 61
                    check (not (shown()) && not (waiting())) "The tip came back after a click on its tab"
                    hover 460
                    rest()
                    send WindowMessages.WM_MOUSELEAVE Pt.empty
                    check (not (shown()) && not (waiting())) "The tip stayed after the pointer left the strip"
                    for id in [101;102;103] do titled id ""

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
                // Expanding and collapsing run over a few frames that only move outwards or inwards,
                // and turning back part way starts from where the strip is, not from either end.
                TabReveal.forced <- Some true
                let watch (until:unit -> bool) =
                    let heights = ResizeArray([strip.bounds.size.height])
                    let clock = Diagnostics.Stopwatch.StartNew()
                    while not (until()) && clock.ElapsedMilliseconds<1000L do
                        Application.DoEvents()
                        Threading.Thread.Sleep(2)
                        heights.Add(strip.bounds.size.height)
                    List.ofSeq heights
                let collapsedHeight = strip.bounds.size.height
                let beforeExpand = frames strip
                strip.isShrunk <- false
                let growing = watch (fun () -> strip.bounds.size.height=larger.height && shadow.isVisible)
                check (strip.bounds.size.height=larger.height && shadow.isVisible) "Expanded tabs did not end at full height with their shadow"
                check (frames strip-beforeExpand>=3L && growing |> List.exists(fun h -> h>collapsedHeight && h<larger.height))
                      "Expanding auto-hidden tabs jumped instead of animating"
                check (growing |> List.pairwise |> List.forall(fun (a,b) -> b>=a)) "Expanding tabs shrank on the way"
                check (strip.bounds.location.y=next.y) "Expanded tabs are not back at the strip's own position"
                strip.isShrunk <- true
                let shrinking = watch (fun () -> strip.bounds.size.height<larger.height)
                let partWay = List.last shrinking
                check (partWay>collapsedHeight && strip.bounds.location.y=next.y+larger.height-partWay)
                      "Collapsing jumped to the bar, or moved away from the window edge"
                strip.isShrunk <- false
                let back = watch (fun () -> strip.bounds.size.height=larger.height)
                check (List.min back>=partWay && strip.bounds.size.height=larger.height) "Turning back part way restarted from the bar"
                strip.isShrunk <- true
                watch (fun () -> strip.bounds.size.height=collapsedHeight) |> ignore
                check (strip.bounds.size.height=collapsedHeight && not shadow.isVisible) "Collapsing did not end at the bar without a shadow"
                TabReveal.forced <- Some false
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
