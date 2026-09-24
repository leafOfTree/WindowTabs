namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

type AppearanceView() =
    let t = SettingsUi.text
    let panel,table = SettingsUi.page()
    let mutable refreshing = false
    let mutable editingDark = ThemeService.currentIsDark()
    let update = Services.settings.updateAppearance
    let paletteTitle = new Label(AutoSize=true,Font=SettingsUi.sectionFont,
                                 Margin=Padding(0,Dpi.scale 16,0,Dpi.scale 8))
    let preset = SettingsUi.choice [|t "Default" "默认";t "Ocean" "海洋";t "Forest" "森林";t "Custom" "自定义"|]
    let presets dark =
        let make (basis:TabPalette) text active hover inactive border =
            { basis with tabTextColor=Color.FromRGB(text);tabActiveBgColor=Color.FromRGB(active)
                         tabHighlightBgColor=Color.FromRGB(hover);tabNormalBgColor=Color.FromRGB(inactive)
                         tabBorderColor=Color.FromRGB(border) }
        if dark then
            [|Theme.darkPalette;Theme.bluePalette
              make Theme.darkPalette 0xE4EEE6 0x19251D 0x2C3E31 0x455C4B 0x718778|]
        else
            [|Theme.lightPalette
              make Theme.lightPalette 0x17324D 0xF5FAFF 0xE0ECF7 0xBCD0E3 0x8BA8C2
              make Theme.lightPalette 0x213C2B 0xF6FBF5 0xE1EEDF 0xC0D5BE 0x8CA889|]
    let rightActions (button:Control) =
        let row = new FlowLayoutPanel(AutoSize=true,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,
                                      Margin=Padding(0,Dpi.scale 8,0,Dpi.scale 8))
        row.Controls.Add(button)
        SettingsUi.add table row
    let paletteForProfile (s:AppearancePreferences) = if editingDark then s.darkPalette else s.lightPalette
    let updatePalette change =
        update(fun s ->
            let current =
                if s.useCustomColors then paletteForProfile s
                elif editingDark then Theme.darkPalette else Theme.lightPalette
            let next = change current
            if next=current then s
            elif editingDark then {s with darkPalette=next;useCustomColors=true}
            else {s with lightPalette=next;useCustomColors=true})
    let preview = new Panel(Height=Dpi.scale 145,Margin=Padding(0,Dpi.scale 4,0,0),
                            AccessibleName=t "File Explorer theme preview" "文件资源管理器主题预览")
    let colorFields : (string * (TabPalette -> Color) * (Color -> TabPalette -> TabPalette)) list = [
        "tabTextColor",(fun p -> p.tabTextColor),(fun v p -> {p with tabTextColor=v})
        "tabActiveBgColor",(fun p -> p.tabActiveBgColor),(fun v p -> {p with tabActiveBgColor=v})
        "tabHighlightBgColor",(fun p -> p.tabHighlightBgColor),(fun v p -> {p with tabHighlightBgColor=v})
        "tabNormalBgColor",(fun p -> p.tabNormalBgColor),(fun v p -> {p with tabNormalBgColor=v})
        "tabBorderColor",(fun p -> p.tabBorderColor),(fun v p -> {p with tabBorderColor=v})
        "tabFlashBgColor",(fun p -> p.tabFlashBgColor),(fun v p -> {p with tabFlashBgColor=v}) ]
    let colors = colorFields |> List.map(fun (key,get,set) -> key,get,set,(new SettingsColorInput(Font=SettingsUi.bodyFont) :> IPropEditor))
    let dimensionFields : (string * int * int * (TabGeometry -> int) * (int -> TabGeometry -> TabGeometry)) list = [
        "tabHeight",12,120,(fun g -> g.height),(fun v g -> {g with height=v})
        "tabMaxWidth",60,1000,(fun g -> g.maxWidth),(fun v g -> {g with maxWidth=v})
        "tabOverlap",-100,0,(fun g -> g.overlap),(fun v g -> {g with overlap=v})
        "tabIndentNormal",0,1000,(fun g -> g.indentNormal),(fun v g -> {g with indentNormal=v})
        "tabIndentFlipped",0,1000,(fun g -> g.indentFlipped),(fun v g -> {g with indentFlipped=v}) ]
    let dimensions = dimensionFields |> List.map(fun (key,minValue,maxValue,get,set) ->
        key,get,set,new SettingsNumberInput(Minimum=decimal minValue,Maximum=decimal maxValue,Font=SettingsUi.bodyFont))
    let refresh() =
        refreshing <- true
        try
            editingDark <- ThemeService.currentIsDark()
            paletteTitle.Text <-
                if editingDark then t "Dark theme · Tab colours" "深色主题 · 标签配色"
                else t "Light theme · Tab colours" "浅色主题 · 标签配色"
            let settings = Services.settings.appearance
            let palette =
                if settings.useCustomColors then paletteForProfile settings
                elif editingDark then Theme.darkPalette else Theme.lightPalette
            let values (p:TabPalette) =
                [p.tabTextColor;p.tabActiveBgColor;p.tabHighlightBgColor;p.tabNormalBgColor;p.tabBorderColor;p.tabFlashBgColor]
                |> List.map(fun c -> c.ToArgb())
            preset.SelectedIndex <- presets editingDark |> Array.tryFindIndex(fun candidate -> values candidate=values palette) |> Option.defaultValue 3
            for key,read,write,editor in colors do
                editor.value <- box(read palette)
            let geometry = settings.geometry
            for key,read,write,editor in dimensions do
                let value = decimal (read geometry)
                // Opening the page must not rewrite a legacy out-of-range value.
                editor.Minimum <- min editor.Minimum value
                editor.Maximum <- max editor.Maximum value
                editor.Value <- value
            preview.Height <- max (Dpi.scale 145) (ThemeService.currentAppearance().scaled.tabHeight+Dpi.scale 120)
            preview.Invalidate()
        finally refreshing <- false

    do
        SettingsUi.section table (t "Theme" "主题")
        SettingsUi.add table (SettingsBindings.themeTiles())
        SettingsUi.add table preview
        preview.Paint.Add(fun e ->
            let p = SettingsColors.current()
            let dark = ThemeService.currentIsDark()
            e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
            use frame = SettingsShapes.rounded (RectangleF(0.5f,0.5f,float32(preview.Width-1),float32(preview.Height-1))) (float32(Dpi.scale 10))
            use frameFill = new SolidBrush(if dark then Color.FromRGB(0x343432) else Color.FromRGB(0xE8E8E5))
            use border = new Pen(p.border)
            e.Graphics.FillPath(frameFill,frame)
            e.Graphics.DrawPath(border,frame)
            let appearance = ThemeService.currentAppearance().scaled
            let height = max 12 appearance.tabHeight
            let width = max 100 (preview.ClientSize.Width-Dpi.scale 32)
            let left = Dpi.scale 16
            let top = Dpi.scale 14
            let bodyTop = top+height+2
            let bodyHeight = preview.Height-bodyTop-Dpi.scale 14
            let fill color (rect:Rectangle) =
                use brush = new SolidBrush(color)
                e.Graphics.FillRectangle(brush,rect)
            let contentColor = if dark then Color.FromRGB(0x202020) else Color.White
            let chromeColor = if dark then Color.FromRGB(0x292929) else Color.FromRGB(0xF3F3F1)
            let placeholderColor = if dark then Color.FromRGB(0x555552) else Color.FromRGB(0xC8C8C3)
            fill contentColor (Rectangle(left,bodyTop,width,bodyHeight))
            let toolbarHeight = Dpi.scale 28
            fill chromeColor (Rectangle(left,bodyTop,width,toolbarHeight))
            fill placeholderColor (Rectangle(left+Dpi.scale 12,bodyTop+Dpi.scale 13,min (Dpi.scale 180) (width/2),Dpi.scale 7))
            let sidebarWidth = min (Dpi.scale 128) (width/3)
            fill chromeColor (Rectangle(left,bodyTop+toolbarHeight,sidebarWidth,bodyHeight-toolbarHeight))
            for index in 0..1 do
                let bounds = Rectangle(left+Dpi.scale 6,bodyTop+toolbarHeight+Dpi.scale (3+index*24),sidebarWidth-Dpi.scale 12,Dpi.scale 23)
                if index=1 then fill p.selection bounds
                fill placeholderColor (Rectangle(bounds.X+Dpi.scale 8,bounds.Y+Dpi.scale 10,
                                                 max 1 (bounds.Width-Dpi.scale (26+index*8)),Dpi.scale 6))
            for index in 0..1 do
                let x = left+sidebarWidth+Dpi.scale 16
                let y = bodyTop+toolbarHeight+Dpi.scale (3+index*24)
                fill placeholderColor (Rectangle(x,y+Dpi.scale 4,Dpi.scale 16,Dpi.scale 17))
                fill placeholderColor (Rectangle(x+Dpi.scale 28,y+Dpi.scale 9,
                                                 min (Dpi.scale (150-index*24)) (max 1 (width-sidebarWidth-Dpi.scale 64)),Dpi.scale 7))
            e.Graphics.DrawRectangle(border,left,bodyTop,width-1,bodyHeight)
            e.Graphics.DrawLine(border,left,bodyTop+toolbarHeight,left+width-1,bodyTop+toolbarHeight)
            let info caption : TabDisplayInfo = {
                bgColor=None; text=caption; icon=SystemIcons.Application
                textFont=SettingsUi.bodyFont; textBrush=SystemBrushes.MenuText }
            let ts : TabStripSprite<int> = {
                tabs=Map2(List2([1,info (t "Active tab" "活动标签");2,info (t "Hovered tab" "悬停标签");3,info (t "Inactive tab" "非活动标签")]))
                lorder=List2([1;2;3]);zorder=List2([1;2;3]);size=Sz(width,height+2)
                slide=None;direction=TabUp;alignment=TabLeft;onlyIcons=false;transparent=true
                appearance=appearance;hover=Some(2,TabBackground);captured=None }
            use bitmap = ts.render.bitmap
            e.Graphics.DrawImageUnscaled(bitmap,left,top))
        let paletteHeader = new Panel(Height=Dpi.scale 36,Margin=Padding(0,Dpi.scale 16,0,Dpi.scale 8))
        paletteTitle.AutoSize <- false
        paletteTitle.Dock <- DockStyle.Fill
        paletteTitle.TextAlign <- ContentAlignment.MiddleLeft
        preset.Name <- "palette-preset"
        preset.AccessibleName <- t "Colour preset" "配色预设"
        preset.Width <- Dpi.scale 180
        preset.Dock <- DockStyle.Right
        paletteHeader.Controls.Add(paletteTitle)
        paletteHeader.Controls.Add(preset)
        SettingsUi.add table paletteHeader
        preset.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && preset.SelectedIndex>=0 && preset.SelectedIndex<3 then
                updatePalette(fun _ -> (presets editingDark).[preset.SelectedIndex]))
        let colorsCard = new SettingsCard()
        SettingsUi.add table colorsCard
        for key,read,write,editor in colors do
            editor.control.Width <- Dpi.scale 180
            SettingsUi.settingRow colorsCard key editor.control
        let reset = SettingsUi.button (t "Reset this palette" "重置当前配色")
        reset.Click.Add(fun _ -> updatePalette(fun _ -> (if editingDark then Theme.darkPalette else Theme.lightPalette)))
        rightActions reset
        let layoutCard = SettingsUi.sectionCard table (t "Tab layout" "标签布局")
        SettingsUi.note layoutCard (t "Sizes stay the same when you switch themes." "切换主题不会改变这些尺寸。")
        for key,read,write,editor in dimensions do SettingsUi.settingRow layoutCard key editor
        let resetLayout = SettingsUi.button (t "Reset tab layout" "重置标签布局")
        resetLayout.Click.Add(fun _ ->
            update Theme.resetLayout)
        rightActions resetLayout
        refresh()
        for key,read,write,editor in colors do
            editor.changed.Add(fun () ->
                if not refreshing then
                    updatePalette(write (editor.value :?> Color)))
        for key,read,write,editor in dimensions do
            editor.ValueChanged.Add(fun _ ->
                if not refreshing then
                    update(fun s -> {s with geometry=write (int editor.Value) s.geometry}))
        ThemeBinding.watch panel refresh

    interface ISettingsView with
        member _.key = AppearanceSettings
        member _.title = t "Appearance" "外观"
        member _.control = panel :> Control
