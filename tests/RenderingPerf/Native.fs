module NativeRenderingPerf

open System
open System.Drawing
open System.IO
open Bemo
open Newtonsoft.Json.Linq

/// Native strip processing and uploads, using off-screen helper HWNDs. No real
/// application windows, input hooks, foreground changes or user settings.
let run (measure:string -> int -> (int -> unit) -> JObject) iterations =
    let results = JArray()
    let originalDirectory = Environment.CurrentDirectory
    let isolated = Path.Combine(originalDirectory,"settings-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true,saveDelay=0)
        for dpi in [96;144] do
            Dpi.set dpi
            let monitor = { new ITabStripMonitor with
                                member _.tabClick _ = ()
                                member _.tabActivate _ = ()
                                member _.tabClose _ = ()
                                member _.tabMoved(_,_) = ()
                                member _.windowMsg _ = () }
            let strip = new TabStrip(monitor)
            try
                strip.setTabAppearance({
                    tabHeight=Dpi.scale 26;tabMaxWidth=Dpi.scale 210;tabOverlap=0;tabHeightOffset=0
                    tabIndentFlipped=0;tabIndentNormal=0;tabTextColor=Color.Black;tabFlashBgColor=Color.Orange
                    tabNormalBgColor=Color.FromArgb(204,204,204);tabActiveBgColor=Color.White
                    tabHighlightBgColor=Color.FromArgb(228,228,228);tabBorderColor=Color.FromArgb(168,168,168) })
                strip.setDefaultAlignment "Left"
                for id in 1..5 do
                    let tab = Tab(IntPtr(100+id))
                    strip.addTab(tab)
                    strip.setTabInfo(tab,{
                        text=sprintf "Window %d" id;isRenamed=false
                        iconSmall=SystemIcons.Application;iconBig=SystemIcons.Application
                        preview=fun () -> Img(Sz(1,1)) })
                let size = Dpi.scaleSize(Sz(1050,28))
                let placement x = {showInside=false;bounds=Rect(Pt(-20000+x,-20000),size)}
                strip.setPlacement(placement 0)
                strip.visible <- true
                let movePointer x =
                    let packed = (Dpi.scale 14 <<< 16) ||| Dpi.scale x
                    WinUserApi.SendMessage(strip.hwnd,WindowMessages.WM_MOUSEMOVE,IntPtr.Zero,IntPtr(packed)) |> ignore
                results.Add(measure (sprintf "5tabs-%ddpi-native-same-tab-mousemove" dpi) iterations (fun i -> movePointer (60+i%40)))
                results.Add(measure (sprintf "5tabs-%ddpi-native-cross-tab-mousemove" dpi) iterations (fun i -> movePointer (60+(i%5)*210)))
                results.Add(measure (sprintf "5tabs-%ddpi-native-window-move" dpi) iterations (fun i ->
                    strip.setPlacement(placement (i%400))
                    // The decorator repeats this assignment when its owner moves.
                    strip.visible <- true))
                results.Add(measure (sprintf "5tabs-%ddpi-native-window-resize" dpi) iterations (fun i ->
                    strip.setPlacement({showInside=false;bounds=Rect(Pt(-20000,-20000),
                        Dpi.scaleSize(Sz(800+i%200,28)))})))
                strip.setPlacement(placement 0)
                results.Add(measure (sprintf "5tabs-%ddpi-native-tab-slide" dpi) iterations (fun i ->
                    strip.slide <- Some(Tab(IntPtr(101)),Dpi.scale (i%400))))
                strip.slide <- None
                // Auto-hide listeners can request the existing expanded state.
                results.Add(measure (sprintf "5tabs-%ddpi-native-repeat-expanded" dpi) iterations (fun _ ->
                    strip.isShrunk <- false))
                let image = strip.renderTs(None)
                try
                    let mask = TabShadow.silhouette image.bitmap
                    let renderer = TabShadow.Renderer()
                    results.Add(measure (sprintf "5tabs-%ddpi-shadow-rebuild-only" dpi) iterations (fun _ ->
                        use bitmap = renderer.Render(image.width,image.height,mask,Dpi.scale 15,TabUp)
                        ()))
                finally image.bitmap.Dispose()
                let order = [for id in 1..5 -> Tab(IntPtr(100+id))]
                results.Add(measure (sprintf "5tabs-%ddpi-native-click-selection-updates" dpi) iterations (fun i ->
                    let tab = order.[i%5]
                    // Click clears attention, then the foreground/shell events
                    // update z-order and information. No foreign app activation.
                    strip.setTabBgColor(tab,None)
                    strip.zorder <- List2((order |> List.filter ((<>) tab))@[tab])
                    strip.setTabInfo(tab,strip.tabInfo(tab))))
                results.Add(measure (sprintf "5tabs-%ddpi-native-repeat-selection-updates" dpi) iterations (fun _ ->
                    let tab = order.[4]
                    strip.setTabBgColor(tab,None)
                    strip.zorder <- List2(order)))
                let popup = Dpi.scaleSize(Sz(640,280))
                results.Add(measure (sprintf "switcher-%ddpi-shadow-cache-miss" dpi) iterations (fun i ->
                    TaskSwitchShadowCache.get dpi (popup.width+i%6) popup.height |> ignore))
                results.Add(measure (sprintf "switcher-%ddpi-shadow-cache-hit" dpi) iterations (fun _ ->
                    TaskSwitchShadowCache.get dpi popup.width popup.height |> ignore))
            finally strip.destroy()
    finally
        Environment.CurrentDirectory <- originalDirectory
        (InvokerService.invoker :> IDisposable).Dispose()
    results
