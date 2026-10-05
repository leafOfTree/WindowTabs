// Run through tests/Run-Tests.ps1 -Suites TabShadow (STA + Application.Run).
// Creates temporary test HWNDs outside the desktop; does not alter running groups.
#r "System.Drawing"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"

open System
open System.Drawing
open System.Drawing.Imaging
open System.Runtime.InteropServices
open Bemo
open Bemo.Win32.Forms

let referenceShadow width height (mask:byte[]) padding =
    let w = width + padding * 2
    let h = height + padding
    let sigma = float padding / 3.0
    let weights = Array.init (padding * 2 + 1) (fun i ->
        let d = float (i - padding)
        exp (-d * d / (2.0 * sigma * sigma)))
    let total = Array.sum weights
    let kernel = weights |> Array.map (fun v -> v / total)
    let sample x y =
        let sx = x - padding
        let sy = y - padding
        if sx < 0 || sx >= width || sy < 0 || sy >= height then 0.0
        else float mask.[sy * width + sx] / 255.0
    let horizontal = Array.zeroCreate<float> (w * h)
    for y in padding .. h - 1 do
        for x in 0 .. w - 1 do
            let mutable v = 0.0
            for k in -padding .. padding do
                v <- v + sample (x + k) y * kernel.[k + padding]
            horizontal.[y * w + x] <- v
    let pixels = Array.zeroCreate<byte> (w * h * 4)
    // Fade the side tails into the window junction. No bottom shadow row.
    let fadeHeight = max 1 (padding / 3)
    for y in 0 .. h - 1 do
        let fade = min 1.0 (float (h - 1 - y) / float fadeHeight)
        for x in 0 .. w - 1 do
            let mutable v = 0.0
            for k in -padding .. padding do
                let sy = y + k
                if sy >= 0 && sy < h then
                    v <- v + horizontal.[sy * w + x] * kernel.[k + padding]
            // The helper is owned by (and above) the strip; cut the tabs out.
            let exterior = 1.0 - sample x y
            pixels.[(y * w + x) * 4 + 3] <- byte (Math.Round(64.0 * v * exterior * fade))
    let bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb)
    let data = bitmap.LockBits(Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb)
    try
        for y in 0 .. h - 1 do
            Marshal.Copy(pixels, y * w * 4, IntPtr.Add(data.Scan0, y * data.Stride), w * 4)
    finally
        bitmap.UnlockBits(data)
    bitmap


let main () =
    let check condition message = if not condition then failwith message
    let pixels (bitmap:Bitmap) =
        let data = bitmap.LockBits(Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb)
        try
            let bytes = Array.zeroCreate<byte> (bitmap.Width*bitmap.Height*4)
            for y in 0..bitmap.Height-1 do
                Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),bytes,y*bitmap.Width*4,bitmap.Width*4)
            bytes
        finally bitmap.UnlockBits(data)
    let renderer = TabShadow.Renderer()
    let random = Random(42)
    // Compare every alpha value with the original convolution, including
    // partial alpha, direction changes and reused buffers after larger frames.
    for width,height,padding in [315,42,22;160,26,15;40,12,3;280,35,20;60,18,8] do
      for partial in [false;true] do
        let mask = Array.init (width*height) (fun i ->
            if partial then byte(random.Next(256))
            elif i%width>width/5 && i%width<width*4/5 && i/width>1 then 255uy else 0uy)
        for direction in [TabUp;TabDown] do
            let input = if direction=TabUp then mask else Array.init mask.Length (fun i -> mask.[(height-1-i/width)*width+i%width])
            use expected = referenceShadow width height input padding
            if direction=TabDown then expected.RotateFlip(RotateFlipType.RotateNoneFlipY)
            use actual = renderer.Render(width,height,mask,padding,direction)
            check (pixels actual = pixels expected) "Optimized shadow pixels differ from the original convolution"
    let padding = 15
    let width, height = 160, 26
    let mask = Array.init (width * height) (fun i ->
        let x, y = i % width, i / width
        if x >= 20 && x < 140 && y >= 1 && y < height - 1 then 255uy else 0uy)
    use shadow = TabShadow.render width height mask padding
    check (shadow.Width = width + padding * 2 && shadow.Height = height + padding) "Unexpected padding"
    let alpha x y = int (shadow.GetPixel(x, y).A)
    check (alpha (padding + 80) (padding - 1) > 0) "Missing top shadow"
    check (alpha (padding + 19) (padding + 10) > 0) "Missing side shadow"
    for y in 0 .. shadow.Height - 1 do
        for x in 0 .. shadow.Width - 1 do
            check (alpha x y = alpha (shadow.Width - 1 - x) y) "Asymmetric shadow"
            if y = shadow.Height - 1 then check (alpha x y = 0) "Bottom shadow line"
            if x >= padding && x < padding + width && y >= padding then
                if mask.[(y - padding) * width + x - padding] = 255uy then
                    check (alpha x y = 0) "Shadow overlaps a tab"
    for y in 1 .. padding - 1 do
        let previous = alpha (padding + 80) (y - 1)
        let current = alpha (padding + 80) y
        check (current >= previous && current - previous <= 6) "Hard edge in gradient"
    use downShadow = TabShadow.renderForDirection width height mask padding TabDown
    for y in 0 .. downShadow.Height - 1 do
        for x in 0 .. downShadow.Width - 1 do
            check (downShadow.GetPixel(x,y).A = shadow.GetPixel(x,shadow.Height-1-y).A) "Down shadow is not mirrored"
    check (downShadow.GetPixel(padding+80,height+1).A > 0uy) "Missing lower shadow"
    for x in 0 .. downShadow.Width-1 do
        check (downShadow.GetPixel(x,0).A = 0uy) "Top attachment seam"
    use empty = TabShadow.render width height (Array.zeroCreate (width * height)) padding
    check (TabShadow.silhouette empty |> Array.forall ((=) 0uy)) "Empty strip casts a shadow"
    use hitArea = new Bitmap(width, height)
    do
        use g = Graphics.FromImage(hitArea)
        g.Clear(Color.FromArgb(1, 1, 1, 1))
    check (TabShadow.silhouette hitArea |> Array.forall ((=) 0uy)) "Hit-test background casts a shadow"

    // A preview generated by the actual tab renderer, on white and dark backgrounds.
    let appearance : TabAppearanceInfo = {
        tabHeight=26; tabMaxWidth=210; tabOverlap=0; tabHeightOffset=0
        tabIndentFlipped=0; tabIndentNormal=0; tabTextColor=Color.Black; tabFlashBgColor=Color.Orange
        tabNormalBgColor=Color.FromArgb(204,204,204)
        tabActiveBgColor=Color.White; tabHighlightBgColor=Color.FromArgb(228,228,228)
        tabBorderColor=Color.FromArgb(168,168,168) }
    let info text : TabDisplayInfo = {
        bgColor=None; text=text; icon=SystemIcons.Application
        textFont=SystemFonts.MenuFont; textBrush=Brushes.Black }
    let ts : TabStripSprite<int> = {
        tabs=Map2(List2([1,info "Active tab"; 2,info "Another window"]))
        lorder=List2([1;2]); zorder=List2([1;2]); size=Sz(420,28)
        slide=None; direction=TabUp; alignment=TabLeft; onlyIcons=false; held=None
        transparent=true; appearance=appearance; hover=None; captured=None }
    let tabImage = ts.render
    // Short tabs shrink their contents to fit instead of clipping them.
    do
        let heights = [12;18;26]
        use sheet = new Bitmap(440,heights |> List.sumBy(fun h -> h+12))
        use g = Graphics.FromImage(sheet)
        g.Clear(Color.FromArgb(45,48,53))
        heights |> List.fold(fun y h ->
            use font = TabMetrics.font h FontStyle.Regular
            check (font.Height <= max h (SystemFonts.MenuFont.Height)) (sprintf "Text line does not fit a %dpx tab" h)
            let info text : TabDisplayInfo = { bgColor=None; text=text; icon=SystemIcons.Application; textFont=font; textBrush=Brushes.Black }
            let strip = { ts with appearance={ appearance with tabHeight=h }; size=Sz(420,h+1)
                                  tabs=Map2(List2([1,info "Active tab"; 2,info "Another window"])) }
            for _,tab in strip.sprite.children.list do
                for location,child in tab.children.list do
                    let size = match child with :? IconSprite as icon -> icon.size | :? CloseButtonSprite as close -> close.size | _ -> Sz(0,0)
                    check (location.y >= 0 && location.y+size.height <= h) (sprintf "Icon or close button overflows a %dpx tab" h)
            use bitmap = strip.render.bitmap
            g.DrawImage(bitmap,10,y+6)
            y+h+12) 0 |> ignore
        sheet.Save(IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","tab-heights.png"),ImageFormat.Png)
    // Icons only: the icon is centred; a smaller close button shows beside it on the pointed-at
    // tab only, and not at all when the tab is too narrow for both.
    do
        let parts (strip:TabStripSprite<int>) =
            strip.sprite.children.list |> List.map(fun (_,tab) ->
                let tab = tab :?> TabSprite<int>
                let find kind = (tab :> ISprite).children.list |> List.tryPick(fun (location,child:ISprite) -> kind location child)
                let icon = find (fun location child -> match child with :? IconSprite as icon -> Some(location,icon.size) | _ -> None)
                let close = find (fun location child -> match child with :? CloseButtonSprite as close -> Some(location,close.size) | _ -> None)
                tab.id,tab.size,icon.Value,close)
        // As TabStrip sizes icon-only tabs.
        let icons = { ts with onlyIcons=true; appearance={ appearance with tabMaxWidth=Dpi.scale 50 } }
        for _,size,(location,icon),close in parts icons do
            check (abs (location.x+icon.width/2-size.width/2) <= 1) "An icon-only tab does not centre its icon"
            check close.IsNone "An icon-only tab shows a close button without the pointer"
        let pointed = parts { icons with hover=Some(2,TabBackground) }
        for id,size,(icon,iconSize),close in pointed do
            match close with
            | Some(location,closeSize) ->
                check (id=2) "An icon-only tab the pointer is not on shows a close button"
                check (location.x >= icon.x+iconSize.width && location.x+closeSize.width <= size.width) "The close button overlaps the centred icon"
                check (closeSize.width < iconSize.width) "The icon-only close button is not smaller than the icon"
            | None -> check (id<>2) "The pointed-at icon-only tab shows no close button"
        let narrow = parts { icons with appearance={ appearance with tabMaxWidth=Dpi.scale 30 }; hover=Some(2,TabBackground) }
        check (narrow |> List.forall(fun (_,_,_,close) -> close.IsNone)) "A tab too narrow for icon and button shows a close button"
        // The end mark of the minimal bar: 6 logical px in the inactive colour, then the active tab.
        use bar = ts.renderCollapsed.bitmap
        let row = bar.Height/2
        check (bar.GetPixel(Dpi.scale 3,row).R=appearance.tabNormalBgColor.R) "The minimal bar's end mark is missing"
        check (bar.GetPixel(Dpi.scale 9,row).R=appearance.tabActiveBgColor.R) "The minimal bar's end mark is wider than 6 px"
    // Many tabs: titles give way to icons, tabs stop at a clickable width and the first stays in view.
    do
        let strip count = { ts with alignment=TabCenter; size=Sz(420,28)
                                    tabs=Map2(List2([for i in 1..count -> i,info "Window"]))
                                    lorder=List2([1..count]); zorder=List2([1..count]) }
        let tabs (s:TabStripSprite<int>) = s.sprite.children.list |> List.map(fun (location,tab) -> location,(tab :?> TabSprite<int>))
        check (not (strip 3).isCompact && (tabs (strip 3)) |> List.forall(fun (_,tab) -> not tab.onlyIcon)) "Three tabs lost their titles"
        check ((strip 6).isCompact && (tabs (strip 6)) |> List.forall(fun (_,tab) -> tab.onlyIcon)) "Tabs too narrow for a title kept it"
        let crowded = tabs (strip 40)
        let minimum = TabMetrics.iconSide 26 + 2*TabMetrics.scaled 26 6
        check (crowded |> List.forall(fun (_,tab) -> tab.size.width >= minimum)) "Crowded tabs shrank below a clickable width"
        check (crowded |> List.forall(fun (location,_) -> location.x >= 0)) "Crowded centred tabs pushed the first tab out of view"
        let held = { strip 3 with held=Some(100.0,60.0) }
        let first = tabs held |> List.find(fun (_,tab) -> tab.id=1)
        check ((fst first).x=60 && (snd first).size.width=100) "A held layout moved the tabs after a close"
        let overflowing = { strip 5 with held=Some(100.0,60.0) }
        check ((tabs overflowing) |> List.forall(fun (_,tab) -> tab.size.width<100)) "A held layout that no longer fits was kept"
    let originalDpi = Dpi.value()
    try
        for dpi in [96;144;192] do
            Dpi.set dpi
            let close : ISprite =
                { CloseButtonSprite.hover=false; captured=false; foreColor=Color.Black; size=Dpi.scaleSize(Sz(16,16)) } :> ISprite
            let extent = Dpi.scale 16
            use closePixels = close.image.bitmap
            check (closePixels.GetPixel(1,1).A=0uy) "Close hit-test test point is not transparent"
            for x,y in [1,1;extent-2,1;1,extent-2;extent-2,extent-2;extent/2,extent/2] do
                check (not (close.hit(Pt(x,y))).isEmpty) "Transparent area of close button cannot be clicked"
            for x,y in [-1,0;0,-1;extent,0;0,extent] do
                check ((close.hit(Pt(x,y))).isEmpty) "Close button captures neighboring pixels"
            for direction in [TabUp;TabDown] do
                let strip = { ts with appearance=appearance.scaled; size=Dpi.scaleSize(Sz(420,28)); direction=direction }
                use bar = strip.renderCollapsed.bitmap
                check (bar.Height=Dpi.scale 4 && bar.Width=strip.size.width) "Minimal bar does not scale with DPI"
                let expectedOffset = if direction=TabUp then strip.size.height-bar.Height else 0
                check (strip.collapsedOffset=expectedOffset) "Minimal bar is not at the window edge"
                check (bar.GetPixel(Dpi.scale 80,bar.Height/2).A=255uy) "Minimal bar lacks a solid hover target"
                // An active tab at either end of the bar is marked at that end, in the inactive colour;
                // one between others, or alone, is not.
                let sameColor (a:Color) (b:Color) = a.ToArgb()=b.ToArgb()
                let pixel (bar:Bitmap) x = bar.GetPixel(x,bar.Height/2)
                let segment (strip:TabStripSprite<int>) id =
                    strip.sprite.children.list |> List.pick(fun (location,sprite) ->
                        let tab = sprite :?> TabSprite<int>
                        if tab.id=id then Some(location.x,tab.size.width) else None)
                // The gap between tabs stays clear, and the end mark has rounded ends: its outermost
                // pixel is only partly covered.
                let x2,_ = segment strip 2
                check ((pixel bar x2).A=0uy) "The gap between tabs is filled"
                let x,width = segment strip 1
                check ((bar.GetPixel(x,0)).A<255uy) "The end mark has a square end"
                check (sameColor (pixel bar (x+Dpi.scale 3)) appearance.tabNormalBgColor) "An active first tab is not marked at the left end"
                check (sameColor (pixel bar (x+width/2)) appearance.tabActiveBgColor) "The mark reaches the middle of the active tab"
                let lastActive = { strip with lorder=List2([2;1]) }
                use lastBar = lastActive.renderCollapsed.bitmap
                let x,width = segment lastActive 1
                check (sameColor (pixel lastBar (x+width-Dpi.scale 3)) appearance.tabNormalBgColor) "An active last tab is not marked at the right end"
                let three = { strip with tabs=Map2(List2([1,info "One";2,info "Two";3,info "Three"])); lorder=List2([2;1;3]); zorder=List2([1;2;3]) }
                use middleBar = three.renderCollapsed.bitmap
                let x,width = segment three 1
                check ([x+Dpi.scale 3;x+width/2;x+width-Dpi.scale 3] |> List.forall(fun at -> sameColor (pixel middleBar at) appearance.tabActiveBgColor))
                      "An active tab between others is marked"
                let alone = { strip with tabs=Map2(List2([1,info "Only"])); lorder=List2([1]); zorder=List2([1]) }
                use aloneBar = alone.renderCollapsed.bitmap
                let x,width = segment alone 1
                check ([x+Dpi.scale 3;x+width-Dpi.scale 3] |> List.forall(fun at -> sameColor (pixel aloneBar at) appearance.tabActiveBgColor))
                      "A lone tab is marked"
                // A tab calling for attention shows its flashing colour in the bar as well.
                let flashing = { three with tabs=Map2(List2([1,info "One";2,{ info "Two" with bgColor=Some appearance.tabFlashBgColor };3,info "Three"])) }
                use flashingBar = flashing.renderCollapsed.bitmap
                let x,width = segment flashing 2
                check (sameColor (pixel flashingBar (x+width/2)) appearance.tabFlashBgColor) "A flashing tab does not show in the minimal bar"
                check ((strip.tryHit(Pt(Dpi.scale 80,strip.collapsedOffset+bar.Height/2))).IsSome) "Minimal bar cannot reveal its tabs"
                let tabLocation,tabSprite = strip.sprite.children.list |> List.find(fun (_,sprite) -> sprite.children.list |> List.exists(fun (_,child) -> child :? CloseButtonSprite))
                let closeLocation,_ = tabSprite.children.list |> List.find(fun (_,child) -> child :? CloseButtonSprite)
                let point = tabLocation.add(closeLocation).add(Pt(1,1))
                check (strip.tryHit(point) |> Option.exists(fun (_,part) -> part=TabClose)) "Strip does not route the full close-button region to close"
        // For review: the minimal bar in both default themes, over a title bar of the same colour
        // as the active tab, where the mark is all that shows which tab is active.
        do
            Dpi.set originalDpi
            // The active tab first, between the others, and last, in each theme.
            let rows = [ for theme in [Theme.light;Theme.dark] do
                           for order in [[1;2;3];[2;1;3];[2;3;1]] -> theme,order ]
            // Left: over a title bar in the active tab's colour. Right: over another app's blue one.
            let half = Dpi.scale 420+40
            use sheet = new Bitmap(half*2,rows.Length*24)
            use g = Graphics.FromImage(sheet)
            rows |> List.iteri(fun index (theme,order) ->
                let strip = { ts with appearance=theme.scaled; size=Dpi.scaleSize(Sz(420,28))
                                      tabs=Map2(List2([1,info "One";2,info "Two";3,info "Three"])); lorder=List2(order); zorder=List2([1;2;3]) }
                use caption = new SolidBrush(theme.tabActiveBgColor)
                g.FillRectangle(caption,0,index*24,half,24)
                use other = new SolidBrush(Color.FromArgb(40,90,160))
                g.FillRectangle(other,half,index*24,half,24)
                use bar = strip.renderCollapsed.bitmap
                g.DrawImage(bar,20,index*24+10)
                g.DrawImage(bar,half+20,index*24+10))
            sheet.Save(IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","minimal-bar.png"),ImageFormat.Png)
    finally Dpi.set originalDpi
    use tabBitmap = tabImage.bitmap
    use rendered = TabShadow.render tabBitmap.Width tabBitmap.Height (TabShadow.silhouette tabBitmap) padding
    use preview = new Bitmap(540,260)
    do
        use g = Graphics.FromImage(preview)
        g.Clear(Color.White)
        use dark = new SolidBrush(Color.FromArgb(45,48,53))
        g.FillRectangle(dark, 0, 130, 540, 130)
        for y in [55;185] do
            g.FillRectangle(Brushes.WhiteSmoke, 30, y + tabBitmap.Height, 480, 48)
            g.DrawImage(rendered, 60-padding, y-padding)
            g.DrawImage(tabBitmap, 60, y)
    let previewPath = IO.Path.Combine(__SOURCE_DIRECTORY__, "Debug", "shadow-preview.png")
    preview.Save(previewPath, ImageFormat.Png)
    let downTabs = { ts with direction=TabDown }
    use downBitmap = downTabs.render.bitmap
    use downRendered = TabShadow.renderForDirection downBitmap.Width downBitmap.Height (TabShadow.silhouette downBitmap) padding TabDown
    use downPreview = new Bitmap(540,110)
    do
        use g = Graphics.FromImage(downPreview)
        g.Clear(Color.WhiteSmoke)
        g.DrawImage(downRendered, 60-padding, 10)
        g.DrawImage(downBitmap, 60, 10)
    downPreview.Save(IO.Path.Combine(__SOURCE_DIRECTORY__, "Debug", "shadow-down-preview.png"), ImageFormat.Png)
    printfn "Rendering checks passed. Preview: %s" previewPath

    // Real HWNDs outside the desktop: ownership, flags, move/hide/restore and cleanup.
    let os = OS()
    let mutable helper : TabShadowWindow option = None
    let handler (msg:Win32Message) =
        let result = msg.def()
        if msg.msg = WindowMessages.WM_WINDOWPOSCHANGED then
            helper |> Option.iter (fun shadow -> shadow.sync())
        result
    let owner = os.createWindow handler WindowsStyles.WS_POPUP WindowsExtendedStyles.WS_EX_LAYERED
    try
        let foreground = os.foreground.hwnd
        let ownerWindow = os.windowFromHwnd(owner.hwnd)
        let layer = new TabShadowWindow(os, owner.hwnd)
        helper <- Some layer
        try
            ownerWindow.update(tabImage, Pt(-10000,-10000), 255uy)
            layer.update(tabImage, 255uy, TabUp)
            let owned = os.windowsInZorder.list |> List.find (fun w -> w.parent.hwnd = owner.hwnd)
            let hwnd = owned.hwnd
            check owned.isVisible "Shadow not shown"
            check (owned.hasStyleEx WindowsExtendedStyles.WS_EX_TRANSPARENT) "Shadow intercepts clicks"
            check (owned.hasStyleEx WindowsExtendedStyles.WS_EX_NOACTIVATE) "Shadow can activate"
            check (owned.hasStyleEx WindowsExtendedStyles.WS_EX_TOOLWINDOW) "Shadow is not a tool window"
            check (not owned.isTopMost) "Shadow is globally topmost"
            let distance = Dpi.scale 15
            check (owned.location.x = -10000-distance) "Wrong initial position"
            // SetWindowPos notifies the owner; layered bitmap updates refresh
            // both layers explicitly, as TabStrip.update does.
            let move x y =
                WinUserApi.SetWindowPos(owner.hwnd, IntPtr.Zero, x, y, 0, 0,
                    SetWindowPosFlags.SWP_NOSIZE ||| SetWindowPosFlags.SWP_NOACTIVATE |||
                    SetWindowPosFlags.SWP_NOZORDER) |> ignore
            move -9900 -9800
            check (owned.location.x = -9900-distance && owned.location.y = -9800-distance) "Shadow did not follow"
            ownerWindow.hide()
            check (not owned.isVisible) "Shadow left behind after hide"
            ownerWindow.showNoActivate()
            check owned.isVisible "Shadow not restored"
            // Simulate Windows hiding the owned popup after a frame update.
            // The deferred activation refresh must recover even with no new bitmap.
            owned.hide()
            check (not owned.isVisible) "Could not simulate hidden owned popup"
            layer.sync()
            check owned.isVisible "Activation sync did not restore the shadow"
            check (ownerWindow.prevZorder.hwnd = hwnd) "Shadow not adjacent to strip"
            layer.hide()
            move -9800 -9700
            check (not owned.isVisible) "Disabled shadow reappeared"
            ownerWindow.update(tabImage, Pt(-9700,-9600), 128uy)
            layer.update(tabImage, 128uy, TabUp)
            check (owned.location.x = -9700-distance && owned.location.y = -9600-distance) "Layered update did not follow"
            check (os.foreground.hwnd = foreground) "Shadow changed foreground"
            layer.update(tabImage, 255uy, TabDown)
            check (owned.location.y = -9600) "Down shadow offset is wrong"
            move -9600 -9500
            check (owned.location.y = -9500) "Down shadow did not follow"
            layer.update(tabImage, 255uy, TabUp)
            check (owned.location.y = -9500-distance) "Restore did not reset shadow direction"
            (layer :> IDisposable).Dispose()
            helper <- None
            check (not (WinUserApi.IsWindow(hwnd))) "Shadow HWND leaked"
            printfn "Native window lifecycle checks passed."
        finally
            if helper.IsSome then
                helper <- None
                (layer :> IDisposable).Dispose()
    finally
        (owner :?> IDisposable).Dispose()
TestInit.run main
