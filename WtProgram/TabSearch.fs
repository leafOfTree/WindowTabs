namespace Bemo
open System
open System.Drawing
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo.Win32

/// A window the tab search can switch to.
type TabSearchEntry = {
    hwnd:IntPtr
    /// The tab's name: the user's rename, else the window title.
    title:string
    /// The process's file name without .exe.
    program:string
    /// What a search term may match, in lower case: both names and the program.
    keywords:string list
    /// Tabs of one group share a number; groups are numbered from the most recently used.
    group:int
    /// 0 for the most recently used tab, the active tab last.
    recency:int
    /// The virtual desktop the window is on when that is not the one in view, else "".
    desktop:string }

/// The search box's rows, in logical pixels: room for a line of text and a small app icon,
/// eleven of them about as tall as nine of the earlier, roomier rows.
module TabSearchMetrics =
    let rowHeight = 36
    let iconSize = 20
    let visibleRows = 11

module TabSearch =
    /// Keep the earliest position of the strongest match, rather than an earlier weak occurrence.
    let private termMatch (term:string) (text:string) =
        let rec find start firstInside =
            match text.IndexOf(term,start,StringComparison.Ordinal) with
            | -1 -> firstInside |> Option.map(fun at -> 1,at)
            | at when at=0 || not (Char.IsLetterOrDigit text.[at-1]) -> Some(2,at)
            | at -> find (at+1) (firstInside |> Option.orElse (Some at))
        find 0 None

    /// 2 at the start of a word, 1 elsewhere, 0 not at all.
    let quality term text = termMatch term text |> Option.map fst |> Option.defaultValue 0

    /// None when a term is missing; otherwise higher for better matches. Terms are separated by
    /// spaces and may come in any order; case is ignored.
    let private rank (query:string) (entry:TabSearchEntry) =
        let title = entry.title.ToLowerInvariant()
        query.ToLowerInvariant().Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold (fun total term ->
            total |> Option.bind(fun (sum,position) ->
                let best =
                    entry.keywords |> List.choose (termMatch term)
                    |> List.sortBy(fun (quality,at) -> -quality,at)
                    |> List.tryHead
                // Where it stands in the name, when the name has it as well as any: a keyword that
                // has it sooner does not put a row above one named alike.
                let best =
                    match best,termMatch term title with
                    | Some(quality,_),Some(inName,at) when inName=quality -> Some(quality,at)
                    | _ -> best
                best |> Option.map(fun (quality,at) -> sum+quality,position+at))) (Some(0,0))

    let score query entry = rank query entry |> Option.map fst

    let matches query entry = (score query entry).IsSome

    /// 0 for a row named just what was typed, 1 for one whose name starts with it: the whole name
    /// of a command finds that command before a longer one, or a description, that mentions it.
    let private named (query:string) (entry:TabSearchEntry) =
        let typed = query.Trim()
        if typed="" then 2
        elif entry.title.Equals(typed,StringComparison.CurrentCultureIgnoreCase) then 0
        elif entry.title.StartsWith(typed,StringComparison.CurrentCultureIgnoreCase) then 1
        else 2

    /// How well an entry matches, best first: its name, match quality, earlier positions, a window
    /// on the desktop in view, then recency. None when it does not match.
    let order query (entry:TabSearchEntry) =
        rank query entry |> Option.map(fun (quality,position) -> named query entry,-quality,position,(if entry.desktop="" then 0 else 1),entry.recency)

    /// The best match. With nothing typed that is the previous tab, so Enter goes back to it as
    /// Alt+Tab does.
    let best query (entries:TabSearchEntry list) =
        entries
        |> List.choose(fun entry -> order query entry |> Option.map(fun key -> entry,key))
        |> List.sortBy snd
        |> List.tryHead
        |> Option.map fst

    /// The matching entries to list: in their groups while nothing is typed, else best match first.
    let shown query (entries:('a * TabSearchEntry) list) =
        let matching = entries |> List.choose(fun (item,entry) -> order query entry |> Option.map(fun key -> (item,entry),key))
        if String.IsNullOrWhiteSpace query then List.map fst matching
        else matching |> List.sortBy snd |> List.map fst

    /// The program, and the desktop of a window on another one.
    let source (entry:TabSearchEntry) =
        match entry.program,entry.desktop with
        | program,"" -> program
        | "",desktop -> desktop
        | program,desktop -> program+" · "+desktop

    /// Where the typed terms appear in a text, as (start,length) runs to highlight. The terms
    /// typed as one phrase light up as one where the text has them so, space and all.
    let highlights (query:string) (text:string) =
        let terms = query.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
        let phrase = String.Join(" ",terms)
        let found =
            (if terms.Length>1 then phrase::terms else terms)
            |> Seq.collect(fun term ->
                Seq.unfold(fun start ->
                    match text.IndexOf(term,start,StringComparison.OrdinalIgnoreCase) with
                    | -1 -> None
                    | at -> Some((at,at+term.Length),at+1)) 0)
            |> Seq.sort |> List.ofSeq
        // Overlapping runs merge, as with "ab" and "bc" in "abc".
        found
        |> List.fold(fun runs (start,stop) ->
            match runs with
            | (lastStart,lastStop)::rest when start<=lastStop -> (lastStart,max lastStop stop)::rest
            | _ -> (start,stop)::runs) []
        |> List.rev
        |> List.map(fun (start,stop) -> start,stop-start)

    let create hwnd title original program =
        { hwnd=hwnd; title=title; program=program
          keywords=[title;original;program] |> List.filter((<>) "") |> List.distinct |> List.map(fun text -> text.ToLowerInvariant())
          group=0; recency=0; desktop="" }

    /// Where a window on another virtual desktop is: the name the user gave that desktop, or its
    /// number.
    let desktopOf (window:Window) =
        if not (WindowCloaking.IsCloakedByShell window.hwnd) then ""
        else
            match VirtualDesktop.desktopId window.hwnd |> Option.map VirtualDesktop.describe with
            | Some(_,Some name) -> name
            | Some(Some place,None) -> tr (Strings.TabSearch.desktop place)
            | _ -> tr Strings.TabSearch.otherDesktop

    let entry (nameOverride:IntPtr -> string option) (window:Window) =
        let text = window.text
        // Store apps all run in ApplicationFrameHost, which tells them apart no better than nothing.
        let program =
            if window.className="ApplicationFrameWindow" then ""
            else try IO.Path.GetFileNameWithoutExtension(window.pid.exeName) with _ -> ""
        { create window.hwnd (nameOverride window.hwnd |> Option.defaultValue text) text program with desktop=desktopOf window }

    /// Lists groups one after another, the most recently used first, each in its tab order.
    /// zorder gives each window's place in the z-order, 0 at the top.
    let arrange (zorder:IntPtr -> int) (active:IntPtr) (groups:TabSearchEntry list list) =
        let used (entry:TabSearchEntry) = if entry.hwnd=active then Int32.MaxValue else zorder entry.hwnd
        let recency =
            groups |> List.concat |> List.sortBy used |> List.mapi(fun index entry -> entry.hwnd,index) |> Map.ofList
        let listed =
            groups
            |> List.filter(List.isEmpty >> not)
            |> List.sortBy(fun tabs -> tabs |> List.map(fun entry -> zorder entry.hwnd) |> List.min)
            |> List.mapi(fun group tabs -> tabs |> List.map(fun entry -> { entry with group=group; recency=recency.[entry.hwnd] }))
            |> List.concat
        // The window in front last: it is where the search was opened from, seldom where it is
        // going, and the first row is then the one Enter switches to. Still listed, to be found
        // by name and given a key.
        let current,others = listed |> List.partition(fun entry -> entry.hwnd=active)
        others @ current

    /// Every group's tabs and switchable windows without tabs, each listed once.
    let tabs nameOverride (active:IntPtr) (groups:IntPtr list list) =
        let os = OS()
        let windows = os.windowsInZorder.list
        let zorder = windows |> List.mapi(fun index window -> window.hwnd,index) |> Map.ofList
        let grouped = groups |> List.concat |> Set.ofList
        let ungrouped =
            windows |> List.filter(fun window ->
                not(grouped.Contains window.hwnd) && not(TaskbarProxies.contains window.hwnd) &&
                window.isAltTabWindow && not(String.IsNullOrEmpty window.text) &&
                window.text <> "Microsoft Text Input Application" && window.className <> "Windows.UI.Core.CoreWindow")
            |> List.map(fun window -> [window.hwnd])
        let groups = groups @ ungrouped
        groups
        |> List.map(List.map os.windowFromHwnd >> List.filter(fun window -> not (String.IsNullOrEmpty window.text)) >> List.map (entry nameOverride))
        |> arrange (fun hwnd -> zorder.TryFind hwnd |> Option.defaultValue Int32.MaxValue) active

/// How a row's shortcut is set from the palette: the one it has now; what holds a key already,
/// by name, if anything; and setting one, false when another program holds the key.
type ShortcutAssignment = { current:int; usedBy:int -> string option; set:int -> bool }

/// A page of the palette beside the windows: what it lists, with a glyph for its rows, and what
/// choosing a row does. Its entries carry a number of their own in place of a window handle.
type PalettePage = {
    scope:PaletteScope
    /// On the line under the box, where Tab brings it.
    caption:string
    /// The hint in the empty box.
    prompt:string
    /// Said when what is typed matches nothing.
    noMatches:string
    /// Said when the page has nothing to list, nothing typed, such as before any workspace is saved.
    noEntries:string
    glyph:TreeListGlyph
    entries:TabSearchEntry list
    choose:TabSearchEntry -> CommandOutcome
    /// For a row that can be given a shortcut with Alt+Enter.
    shortcut:TabSearchEntry -> ShortcutAssignment option
    /// A row listed in the muted colour that does nothing now, such as a tab command with no
    /// tab in front: there to be found, and to be given a key.
    dimmed:TabSearchEntry -> bool
    /// The right end of a row, read again after every change, as a switch may have turned.
    aside:string -> TabSearchEntry -> PaletteAside
    /// Whether the palette goes out of sight while a row's command runs, so the window it acts
    /// on is in front: not for a setting turned on or off, which the palette stays up to show.
    concealed:TabSearchEntry -> bool }

/// What a page's row shows besides its name: at the right, its detail, such as a value or a
/// page, with a down arrow when the value is picked from a list, or a switch, for a setting that
/// is on or off; just after the name, in the muted colour, its shortcut, as editors list theirs;
/// and a colour, for a row that stands for one, shown as a dot before the name.
/// small: the detail in a smaller type, for a note such as where a row opens rather than a value.
and PaletteAside = { detail:string; switch:bool option; swatch:Color option; shortcut:string; dropdown:bool; small:bool }

module private TabSearchNative =
    let EM_SETCUEBANNER = 0x1501
    [<DllImport("user32.dll", CharSet=CharSet.Unicode)>]
    extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam)

/// A search box in the switcher's look: over the tabs, where Enter or a click switches to the
/// window, or, as the command palette, over pages of commands and the like, where it runs the
/// command. Type to filter. It remembers the chosen window scope; Tab, or clicking the line
/// under the box, goes round the scopes. A command that leaves a
/// choice shows it in place, with Esc or Backspace the way back. Esc or clicking elsewhere
/// closes it. Used once, then disposed.
/// pick: what a chosen window is for, switching to it unless given; within: an area on screen to
/// open over instead of high on the active screen; prompt: the hint in the empty box; rows and
/// width: how many rows show and how wide it is, in logical pixels; pages: the pages beside the
/// windows, or in their place with windows false; scope: the one to open on.
type TabSearchForm(tabs:TabSearchEntry list, group:IntPtr list, ?settings:ISettings,
                   ?pick:IntPtr -> unit, ?within:Rectangle, ?prompt:string, ?rows:int, ?width:int,
                   ?pages:PalettePage list, ?scope:PaletteScope, ?windows:bool,
                   ?windowShortcut:TabSearchEntry -> ShortcutAssignment option, ?windowShortcutText:TabSearchEntry -> string) =
    let rowHeight = TabSearchMetrics.rowHeight
    let visibleRows = defaultArg rows TabSearchMetrics.visibleRows
    let windows = defaultArg windows true
    /// Alt+Enter on a window: the key given to it, when the box is told how; and the key it has.
    let windowShortcut = defaultArg windowShortcut (fun _ -> None)
    let windowShortcutText = defaultArg windowShortcutText (fun _ -> "")
    let palette = SettingsColors.current()
    let windowPrompt = defaultArg prompt (tr Strings.TabSearch.prompt)
    let pages = defaultArg pages []
    let requested = scope
    let pick = defaultArg pick (fun hwnd -> OS().windowFromHwnd(hwnd).setForegroundOrRestore(false))
    let area =
        match within with
        | Some box -> Screen.FromRectangle(box).WorkingArea
        | None -> Screen.FromHandle(WinUserApi.GetForegroundWindow()).WorkingArea
    let padding = Dpi.scale 12
    let radius = Dpi.scale 12
    let boxHeight = Dpi.scale 40
    let scopeHeight = Dpi.scale 34
    let ended = Event<unit>()
    let mutable closed = false
    /// Out of sight while a command runs, so the window it acts on is in front: not closed.
    let mutable hiding = false
    let mutable shadow : TaskSwitchShadow option = None
    let items =
        tabs |> List.map(fun entry ->
            let item = TreeListItem(entry.title,Values=[|"";TabSearch.source entry|])
            match ImgHelper.windowIcon (OS().windowFromHwnd entry.hwnd) with
            | Some icon -> item.Icon <- icon
            | None -> item.Glyph <- WindowGlyph
            item,entry) |> Array.ofList
    let groupItems = items |> Array.filter(fun (_,entry) -> List.contains entry.hwnd group)
    let pageItems =
        pages |> List.map(fun page ->
            page.scope,
            page.entries |> List.map(fun entry -> TreeListItem(entry.title,Values=[|"";""|],Glyph=page.glyph,Dimmed=page.dimmed entry),entry) |> Array.ofList)
        |> Map.ofList
    let preferred =
        settings |> Option.map(fun api -> api.getValue("tabSearchScope") :?> string)
        |> Option.defaultValue SettingsCatalog.tabSearchScopeDefault
        |> SettingsCatalog.normalizeTabSearchScope
    /// Only when the group is worth searching on its own and is not every tab.
    let canSwitchScope = groupItems.Length>1 && groupItems.Length<items.Length
    let initialWindowScope = if groupItems.Length>1 && preferred<>"All" then GroupTabs else AllTabs
    /// In Tab order: the group and every window, or the one of them there is, then the pages;
    /// the pages alone for the command palette.
    let scopes =
        (if not windows then [] elif canSwitchScope then [GroupTabs;AllTabs] else [initialWindowScope]) @ [for page in pages -> page.scope]
    let mutable scope =
        match requested with
        | Some wanted when List.contains wanted scopes -> wanted
        | _ -> if windows then initialWindowScope else List.head scopes
    /// The choices a command left, over the scope's rows while they are up: the command's name,
    /// the rows, what choosing one does, and the search typed before, for the way back.
    let mutable choices : (string * (TreeListItem * TabSearchEntry)[] * (TabSearchEntry -> CommandOutcome) * string) list = []
    /// A value asked for, on top of the choices with no rows of its own, with why the last one
    /// typed was not taken.
    let mutable asking : (CommandQuestion * string option) option = None
    let rowsOf target = match target with GroupTabs -> groupItems | AllTabs -> items | other -> pageItems.[other]
    let scoped() = match choices with (_,rows,_,_)::_ -> rows | [] -> rowsOf scope
    let page() = pages |> List.tryFind(fun page -> page.scope=scope)
    let currentPrompt() =
        match asking,choices,page() with
        | Some(question,_),_,_ -> question.prompt
        | None,_::_,_ -> tr Strings.TabSearch.choicePrompt
        | None,[],Some page -> page.prompt
        | None,[],None -> windowPrompt
    let currentEmpty (query:string) =
        match asking,choices,page() with
        | Some(question,error),_,_ -> error |> Option.defaultValue question.hint
        | None,_::_,_ -> tr Strings.TabSearch.noChoices
        | None,[],Some page -> if page.entries.IsEmpty && String.IsNullOrWhiteSpace query then page.noEntries else page.noMatches
        | None,[],None -> tr Strings.TabSearch.noMatches
    let input = new TextBox(Font=SettingsUi.bodyFont(),BorderStyle=BorderStyle.None,Tag="search-input",
                            BackColor=palette.surface,ForeColor=palette.text,
                            AccessibleName=windowPrompt)
    // No outline: a line under it parts the box from the rows, as a launcher's search does.
    let searchBox = new SettingsSearchBox(input,Dock=DockStyle.Top,Height=boxHeight,Underlined=true)
    /// Room between that line and the first row's highlight.
    let underGap = Dpi.scale 6
    /// Smaller than the segments' text, as key caps are.
    let keyFont = SettingsUi.font "Segoe UI" 8.5f FontStyle.Regular
    let caption target =
        match target with
        | GroupTabs -> tr (Strings.TabSearch.groupTabs groupItems.Length)
        | AllTabs -> tr (Strings.TabSearch.allTabs items.Length)
        | other -> pages |> List.tryFind(fun page -> page.scope=other) |> Option.map(fun page -> page.caption) |> Option.defaultValue ""
    /// Where a key's caps go: shorter than the segments, and in the middle of the line.
    let keyHeight() = Dpi.scale 20
    let keyCap x (label:string) =
        Rectangle(x,(scopeHeight-keyHeight())/2,KeyCaps.width keyFont (keyHeight()) label,keyHeight())
    /// One switch for the scope: the listed rows' segment is filled, the others are where Tab or a
    /// click goes, and the key cap beside them names the key. A single segment when there is no choice.
    let segments() =
        let height = scopeHeight-Dpi.scale 8
        let top = (scopeHeight-height)/2
        let width text = TextRenderer.MeasureText(text,SettingsUi.bodyFont(),Size.Empty,TextFormatFlags.NoPadding).Width+Dpi.scale 24
        let inset = Dpi.scale 2
        let placed,right =
            scopes |> List.mapFold(fun x target ->
                let bounds = Rectangle(x,top+inset,width (caption target),height-inset*2)
                (target,bounds),bounds.Right) inset
        let outline = Rectangle(0,top,right+inset,height)
        let key = keyCap (outline.Right+Dpi.scale 12) "Tab"
        placed,outline,(if scopes.Length>1 then Some key else None)
    /// The way back from a command's choices: its name, and the key that returns.
    /// Where the listed rows' names start, once the list is made.
    let mutable rowNameLeft = fun () -> Dpi.scale 10
    /// The caption where the rows' names start, the arrow back in the room before it, and the key.
    let breadcrumb() =
        let text = match choices with (caption,_,_,_)::_ -> caption | [] -> ""
        let left = rowNameLeft()
        let width = TextRenderer.MeasureText(text,SettingsUi.bodyFont(),Size.Empty,TextFormatFlags.SingleLine).Width
        Rectangle(left,0,width,scopeHeight),keyCap (left+width+Dpi.scale 8) "Esc"
    /// Whether the rows in view can be given shortcuts, said at the line's right end.
    let canAssign() =
        choices.IsEmpty &&
        (match page() with
         | Some page -> page.entries |> List.exists(fun entry -> (page.shortcut entry).IsSome)
         | None -> scoped() |> Array.exists(fun (_,entry) -> (windowShortcut entry).IsSome))
    /// The shortcut editor over the chosen row while one is being set, with its row.
    let mutable assigning : (TreeListItem * SettingsShortcutInput) option = None
    let mutable hovered : PaletteScope option = None
    let scopeLine =
        { new Panel(Dock=DockStyle.Top,Height=scopeHeight,BackColor=palette.surface) with
            override this.OnPaint(e) =
                base.OnPaint(e)
                let p = SettingsColors.current()
                let g = e.Graphics
                g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                let rounded (bounds:Rectangle) radius =
                    SettingsShapes.rounded (RectangleF(float32 bounds.X+0.5f,float32 bounds.Y+0.5f,float32 bounds.Width-1.0f,float32 bounds.Height-1.0f)) (float32(Dpi.scale radius))
                let centred = TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter
                // A key, not another segment: its outline alone, as keys are shown everywhere.
                let drawKey (cap:Rectangle) (label:string) = KeyCaps.draw g keyFont cap.X cap.Y cap.Height label p |> ignore
                match choices with
                | (caption,_,_,_)::_ ->
                    let bounds,key = breadcrumb()
                    TextRenderer.DrawText(g,caption,SettingsUi.bodyFont(),bounds,p.text,
                                          TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.EndEllipsis)
                    // A chevron a little shorter than the text's capitals, in the margin before the names.
                    let s = float32(Dpi.scaleF 3.5)
                    let cx,cy = float32(Dpi.scale 3)+s/2.0f,float32 scopeHeight/2.0f
                    use pen = new Pen(p.text,float32(Dpi.scaleF 1.4),StartCap=Drawing2D.LineCap.Round,EndCap=Drawing2D.LineCap.Round,LineJoin=Drawing2D.LineJoin.Round)
                    g.DrawLines(pen,[|PointF(cx+s/2.0f,cy-s);PointF(cx-s/2.0f,cy);PointF(cx+s/2.0f,cy+s)|])
                    drawKey key "Esc"
                | [] ->
                    let placed,outline,key = segments()
                    use frame = rounded outline 8
                    use border = new Pen(p.border)
                    g.DrawPath(border,frame)
                    for target,bounds in placed do
                        let current = target=scope
                        if current then
                            use shape = rounded bounds 6
                            use fill = new SolidBrush(if SystemInformation.HighContrast then SystemColors.Highlight else p.selection)
                            g.FillPath(fill,shape)
                        let color =
                            if current && SystemInformation.HighContrast then SystemColors.HighlightText
                            elif current || hovered=Some target then p.text
                            else p.muted
                        TextRenderer.DrawText(g,caption target,SettingsUi.bodyFont(),bounds,color,centred)
                    key |> Option.iter(fun cap -> drawKey cap "Tab") }
    /// Under the rows, at the right: the key that sets a row's shortcut, named, as editors put it.
    let footerHeight = if pages.IsEmpty && (tabs |> List.forall(fun entry -> (windowShortcut entry).IsNone)) then 0 else Dpi.scale 28
    let footer =
        { new Panel(Dock=DockStyle.Bottom,Height=footerHeight,BackColor=palette.surface) with
            override this.OnPaint(e) =
                base.OnPaint(e)
                if canAssign() then
                    let p = SettingsColors.current()
                    let g = e.Graphics
                    g.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                    let label = tr Strings.TabSearch.setShortcut
                    let labelWidth = TextRenderer.MeasureText(label,keyFont,Size.Empty,TextFormatFlags.NoPadding).Width
                    let height = keyHeight()
                    let width = KeyCaps.width keyFont height "Alt+Enter"
                    let left = this.ClientSize.Width-Dpi.scale 4-width
                    KeyCaps.draw g keyFont left ((this.ClientSize.Height-height)/2) height "Alt+Enter" p |> ignore
                    TextRenderer.DrawText(g,label,keyFont,Rectangle(left-Dpi.scale 8-labelWidth,0,labelWidth+Dpi.scale 4,this.ClientSize.Height),
                                          (if SystemInformation.HighContrast then SystemColors.WindowText else p.accent),
                                          TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter) }
    let programColumn = TreeListColumn("",160,TextColumn,RightAligned=true)
    /// Each right-end text's width, measured once: the rows are measured again at every keystroke.
    let asideWidths = Collections.Generic.Dictionary<string,int>()
    let list = new SettingsTreeList([TreeListColumn("",0,TextColumn);programColumn],
                                    ShowHeader=false,ShowExpanders=false,ExpandOnDoubleClick=false,
                                    RowHeight=rowHeight,IconSize=TabSearchMetrics.iconSize,TabStop=false,KeepScrollRoom=true,
                                    ScrollBarWidth=padding-1,
                                    BackColor=palette.surface,ForeColor=palette.text)
    do rowNameLeft <- fun () -> match Seq.tryHead list.Roots with Some item -> list.NameLeft item | None -> Dpi.scale 10
    let empty = new Label(Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Visible=false,
                          Font=SettingsUi.bodyFont(),BackColor=palette.surface,ForeColor=palette.muted,
                          Text=tr Strings.TabSearch.noMatches)
    /// Where a value is typed, under the line naming what asks for it: the settings pages' box,
    /// with what is accepted under it, or why the last value was not.
    let askPanel = new Panel(Dock=DockStyle.Fill,Visible=false,BackColor=palette.surface)
    let askNote = new Label(AutoSize=false,Font=SettingsUi.bodyFont(),BackColor=palette.surface,ForeColor=palette.muted,
                            UseMnemonic=false,TextAlign=ContentAlignment.TopLeft)
    /// The box the value is typed in, and its text.
    let mutable askBox : (SettingsInputFrame * TextBox) option = None
    /// The colour box, while a colour is asked for: its picker is a popup the palette must not
    /// close for.
    let mutable colorBox : SettingsColorInput option = None
    let form =
        let f =
            { new Form() with
                override this.CreateParams =
                    let createParams = base.CreateParams
                    createParams.ExStyle <- createParams.ExStyle ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW
                                            ||| WindowsExtendedStyles.WS_EX_TOPMOST ||| WindowsExtendedStyles.WS_EX_COMPOSITED
                    createParams
                override this.OnPaint(e) =
                    base.OnPaint(e)
                    e.Graphics.SmoothingMode <- Drawing2D.SmoothingMode.AntiAlias
                    use outline = SettingsShapes.rounded (SettingsShapes.outlineRect this.ClientSize.Width this.ClientSize.Height) (float32 radius)
                    use border = new Pen((SettingsColors.current()).border)
                    e.Graphics.DrawPath(border,outline) }
        // One size whatever is typed or listed, so the box does not jump about while filtering.
        let longest = scopes |> List.map(fun target -> (rowsOf target).Length) |> List.max
        let rows = max 1 (min visibleRows longest)
        // Wide enough for every scope's segment and the key beside them.
        let _,outline,key = segments()
        let segmentsWidth = (match key with Some cap -> cap.Right | None -> outline.Right)+padding*2
        let size = Size(min (max (Dpi.scale (defaultArg width 640)) segmentsWidth) (area.Width-Dpi.scale 32),padding*2+boxHeight+underGap+scopeHeight+rows*Dpi.scale rowHeight+footerHeight)
        f.AutoScaleMode <- AutoScaleMode.None
        f.FormBorderStyle <- FormBorderStyle.None
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        f.TopMost <- true
        f.Font <- SettingsUi.bodyFont()
        f.BackColor <- palette.surface
        f.ForeColor <- palette.text
        f.Padding <- Padding(padding)
        f.ClientSize <- size
        // High on the screen, where a search box is expected, rather than centred; or centred
        // over the area it was opened for, kept on the screen.
        f.Location <-
            match within with
            | Some box ->
                Point(max area.Left (min (area.Right-size.Width) (box.X+(box.Width-size.Width)/2)),
                      max area.Top (min (area.Bottom-size.Height) (box.Y+(box.Height-size.Height)/2)))
            | None -> Point(area.Left+(area.Width-size.Width)/2,area.Top+area.Height/5)
        // Docking runs from the last added: the box on top, then the scope, then the results.
        f.Controls.Add(list)
        f.Controls.Add(empty)
        f.Controls.Add(askPanel)
        f.Controls.Add(footer)
        f.Controls.Add(scopeLine)
        f.Controls.Add(new Panel(Dock=DockStyle.Top,Height=underGap,BackColor=palette.surface))
        f.Controls.Add(searchBox)
        // The list runs on into the right padding, up to the outline, its scroll bar's column as
        // wide as that padding: a row's highlight then stands as far from the right edge as from
        // the left, rather than the padding and the column both.
        let placeList() =
            let top = padding+searchBox.Height+underGap+scopeLine.Height
            let bottom = f.ClientSize.Height-padding-footer.Height
            list.Bounds <- Rectangle(padding,top,max 1 (f.ClientSize.Width-padding-1),max 0 (bottom-top))
        f.Layout.Add(fun _ -> placeList())
        placeList()
        use shape = SettingsShapes.rounded (RectangleF(0.0f,0.0f,float32 size.Width,float32 size.Height)) (float32 radius)
        f.Region <- new Region(shape)
        f
    let entryOf (item:TreeListItem) = scoped() |> Array.pick(fun (row,entry) -> if obj.ReferenceEquals(row,item) then Some entry else None)
    let setPrompt() =
        if input.IsHandleCreated then
            TabSearchNative.SendMessage(input.Handle,TabSearchNative.EM_SETCUEBANNER,IntPtr(1),currentPrompt()) |> ignore
    /// Lists the rows again for a value that changed, the chosen row kept.
    let mutable keptRow : IntPtr option = None
    /// The row chosen at each level a command's choices or value were opened from, chosen again
    /// on the way back rather than the first.
    let mutable backRows : IntPtr option list = []
    /// A setting's values listed: each shown as the choice moves onto it, and whether one has been,
    /// so leaving without choosing puts the ticked value back.
    let mutable previewOptions : CommandChoice list = []
    let mutable previewed = false
    /// The list's choice being set by the palette itself, not moved by the user.
    let mutable selecting = false
    let apply() =
        // The line under the box: the scopes to go round, or the way back from a command's
        // choices. The palette's one page has neither until a command leaves a choice.
        let lineHeight = if windows || scopes.Length>1 || not choices.IsEmpty || asking.IsSome then scopeHeight else 0
        if scopeLine.Height<>lineHeight then scopeLine.Height <- lineHeight
        // In their groups while nothing is typed; then the best matches first, those that can do
        // nothing for the window in front after the rest, unless named just as typed.
        let shown =
            let ranked = TabSearch.shown input.Text (List.ofArray (scoped()))
            if String.IsNullOrWhiteSpace input.Text then Array.ofList ranked
            else
                let typed = input.Text.Trim()
                let later (item:TreeListItem,entry:TabSearchEntry) =
                    item.Dimmed && not (entry.title.Equals(typed,StringComparison.CurrentCultureIgnoreCase))
                let dimmed,live = ranked |> List.partition later
                Array.ofList (live @ dimmed)
        // At the right: the row's own detail (the program, a shortcut) or its switch.
        let aside (entry:TabSearchEntry) : PaletteAside =
            match choices,page() with
            | [],Some page -> page.aside input.Text entry
            | [],None ->
                // A window's key before its program.
                match windowShortcutText entry,TabSearch.source entry with
                | "",detail -> { detail=detail; switch=None; swatch=None; shortcut=""; dropdown=false; small=false }
                | key,"" -> { detail=""; switch=None; swatch=None; shortcut=key; dropdown=false; small=false }
                | key,detail -> { detail=detail; switch=None; swatch=None; shortcut=key; dropdown=false; small=false }
            | _ -> { detail=TabSearch.source entry; switch=None; swatch=None; shortcut=""; dropdown=false; small=false }
        let text (aside:PaletteAside) = aside.detail
        let asides = shown |> Array.map(fun (_,entry) -> aside entry)
        // Titles reclaim the fixed app column's spare space, including after filtering.
        let measure (aside:PaletteAside) =
            let value = text aside
            let key = (if aside.small then "small:" else "")+value
            match asideWidths.TryGetValue key with
            | true,width -> width
            | _ ->
                let font = if aside.small then SettingsUi.font list.Font.FontFamily.Name (list.Font.SizeInPoints*0.86f) FontStyle.Regular else list.Font
                let width = TextRenderer.MeasureText(value,font,Size(Int32.MaxValue,Dpi.scale rowHeight),TextFormatFlags.NoPrefix ||| TextFormatFlags.SingleLine).Width
                asideWidths.[key] <- width
                width
        let programWidth =
            asides |> Array.fold(fun width aside ->
                let switch =
                    if aside.switch.IsSome then Dpi.scale 44
                    elif aside.dropdown then Dpi.scale 18
                    elif aside.swatch.IsSome then Dpi.scale 22
                    else 0
                max width (switch+measure aside)) 0
        programColumn.Width <- max 48 (min 300 (int(Math.Ceiling(float programWidth/Dpi.currentFactor()))+16))
        shown |> Array.iteri(fun index (item,entry) ->
            let aside = asides.[index]
            item.Values.[1] <- text aside
            item.Switch <- aside.switch
            item.Suffix <- aside.shortcut
            item.Dropdown <- aside.dropdown
            item.SmallValue <- aside.small
            // A colour setting's value: its dot before its code at the right, as the settings pages show it.
            item.ValueSwatch <- aside.swatch
            item.Highlights <- [| TabSearch.highlights input.Text entry.title; TabSearch.highlights input.Text item.Values.[1] |])
        list.Roots.Clear()
        list.Roots.AddRange(Array.map fst shown)
        list.Rebuild()
        let chosen =
            // The row chosen before, while the same rows are listed again for a value that changed.
            keptRow |> Option.bind(fun hwnd -> shown |> Array.tryFind(fun (_,candidate) -> candidate.hwnd=hwnd))
            |> Option.orElse (
                TabSearch.best input.Text (shown |> Array.map snd |> List.ofArray)
                |> Option.bind(fun entry -> shown |> Array.tryFind(fun (_,candidate) -> candidate.hwnd=entry.hwnd)))
            |> Option.map fst
        selecting <- true
        try list.SelectedItem <- Option.toObj chosen finally selecting <- false
        chosen |> Option.iter list.EnsureVisible
        empty.Text <- currentEmpty input.Text
        // A value being typed has the place of the rows to itself.
        list.Visible <- shown.Length>0 && asking.IsNone
        empty.Visible <- shown.Length=0 && asking.IsNone
    /// The rows listed again, the chosen one still chosen: a switch turned with Enter, or a value
    /// come in, does not send the choice back to the best match.
    let selectedRow() =
        match list.SelectedItem with
        | null -> None
        | item -> scoped() |> Array.tryPick(fun (row,entry) -> if obj.ReferenceEquals(row,item) then Some entry.hwnd else None)
    let reapply() =
        keptRow <- selectedRow()
        try apply() finally keptRow <- None
    /// Takes the shortcut editor off its row and gives the keys back to the box.
    let endAssign() =
        match assigning with
        | Some(_,editor) ->
            assigning <- None
            list.Controls.Remove(editor)
            editor.Dispose()
            if not closed then input.Focus() |> ignore
        | None -> ()
    /// Closes, switching to the window chosen, if one was.
    /// The ticked value back, when another was shown and none chosen.
    let restorePreview() =
        if previewed then
            previewed <- false
            previewOptions |> List.tryFind(fun option -> option.detail="✓") |> Option.iter(fun option -> option.run() |> ignore)
        previewOptions <- []
    let finish (chosen:IntPtr option) =
        // Switching away deactivates the box, which asks to close again: once is enough.
        if not closed then
            restorePreview()
            closed <- true
            endAssign()
            chosen |> Option.iter pick
            shadow |> Option.iter(fun item -> (item :> IDisposable).Dispose())
            shadow <- None
            form.Hide()
            ended.Trigger()
            // Not inside the form's own Deactivate or key handler.
            let dispose() =
                form.Dispose()
                ImgHelper.disposeItems (Array.map fst items)
            if form.IsHandleCreated then form.BeginInvoke(Action dispose) |> ignore else dispose()
    /// The choices a command left take the rows' place, under its name, until one is chosen or
    /// Esc goes back.
    let push (caption:string) (options:CommandChoice list) =
        endAssign()
        backRows <- selectedRow()::backRows
        let rows =
            options |> List.mapi(fun index option ->
                let entry = { TabSearch.create (IntPtr(index+1)) option.label option.label option.detail with recency=index }
                TreeListItem(option.label,Values=[|"";option.detail|],Swatch=option.swatch),entry)
            |> Array.ofList
        let run (entry:TabSearchEntry) = options.[int entry.hwnd-1].run()
        choices <- (caption,rows,run,input.Text)::choices
        restorePreview()
        previewOptions <- if options |> List.exists(fun option -> option.preview) then options else []
        input.Text <- ""
        setPrompt()
        scopeLine.Invalidate()
        footer.Invalidate()
        // Opened on the value it has now, so moving from it shows the others.
        keptRow <- options |> List.tryFindIndex(fun option -> option.detail="✓") |> Option.map(fun index -> IntPtr(index+1))
        try apply() finally keptRow <- None
    /// The line under the value's box: what is accepted, or in the error colour why it was not.
    let showNote() =
        match asking with
        | Some(question,error) ->
            askNote.Text <- error |> Option.defaultValue question.hint
            askNote.ForeColor <-
                if error.IsNone then palette.muted
                elif SystemInformation.HighContrast then SystemColors.WindowText
                elif ThemeService.currentIsDark() then Color.FromRGB(0xFF99A4) else Color.FromRGB(0xC42B1C)
        | None -> ()
    /// Takes the value's box away and gives the keys back to the search.
    let endAsk() =
        askBox |> Option.iter(fun (box,_) ->
            askPanel.Controls.Remove(box)
            box.Dispose())
        askBox <- None
        colorBox <- None
        input.ReadOnly <- false
        askPanel.Visible <- false
    let pop() =
        asking <- None
        endAsk()
        restorePreview()
        if not closed then input.Focus() |> ignore
        match choices with
        | (_,_,_,query)::rest ->
            choices <- rest
            let back = match backRows with row::more -> backRows <- more; row | [] -> None
            input.Text <- query
            // Back at the end of what was typed, to carry on typing.
            input.Select(query.Length,0)
            setPrompt()
            scopeLine.Invalidate()
            footer.Invalidate()
            keptRow <- back
            try apply() finally keptRow <- None
        | [] -> ()
    /// Enter while a value is asked for: taken, the palette closes; refused, it says why.
    let answer() =
        match asking,askBox with
        | Some(question,_),Some(_,text) ->
            match question.accept (text.Text.Trim()) with
            | None -> finish None
            | Some reason ->
                asking <- Some(question,Some reason)
                showNote()
        | _ -> ()
    /// A value to type: under the name of what asked, the settings pages' box holding the value
    /// it has now, selected, with what is accepted under it. Enter offers what is typed, Esc goes
    /// back, and the search is read only meanwhile.
    let ask (caption:string) (question:CommandQuestion) =
        push caption []
        asking <- Some(question,None)
        let frame,text =
            match question.kind with
            | NumberQuestion(low,high) ->
                let number = new SettingsNumberInput(Minimum=decimal low,Maximum=decimal high,Font=SettingsUi.bodyFont())
                number.Value <- (match Decimal.TryParse question.value with | true,value -> value | _ -> decimal low)
                (number :> SettingsInputFrame),number.TextBox
            | TextQuestion ->
                let text = new TextBox(Font=SettingsUi.bodyFont(),Text=question.value)
                (new SettingsTextInput(text,Width=Dpi.scale SettingsShapes.editorWidth) :> SettingsInputFrame),text
            | ColorQuestion ->
                let color = new SettingsColorInput(Font=SettingsUi.bodyFont())
                (color :> IPropEditor).value <- box (try ColorTranslator.FromHtml question.value with _ -> Color.White)
                colorBox <- Some color
                // Back to the palette once a colour is picked, so a click elsewhere still closes it.
                color.PickerClosed.Add(fun _ -> if not closed && form.Visible then form.Activate())
                // One Esc goes back and one Enter takes the colour, as in the box, rather than the
                // first only closing the picker.
                // On the next turn: the box and its picker go with the value, not inside the
                // picker's own closing.
                color.PickerKey.Add(fun key ->
                    form.BeginInvoke(Action(fun () ->
                        if not closed then
                            if key=Keys.Escape then pop()
                            elif key=Keys.Enter then answer())) |> ignore)
                (color :> SettingsInputFrame),color.TextBox
        frame.PlaceOn(palette.surface)
        // On the palette's surface, the field's own colour: outlined as the search box is.
        frame.Border <- SettingsSearchBox.Outline()
        text.ForeColor <- palette.text
        let gap = Dpi.scale 8
        frame.Location <- Point(Dpi.scale 4,gap)
        askNote.Bounds <- Rectangle(Dpi.scale 6,frame.Bottom+gap,max 1 (askPanel.ClientSize.Width-Dpi.scale 12),askNote.Font.Height*3)
        askNote.Anchor <- AnchorStyles.Left ||| AnchorStyles.Top ||| AnchorStyles.Right
        askPanel.Controls.Add(frame)
        if not (askPanel.Controls.Contains askNote) then askPanel.Controls.Add(askNote)
        askBox <- Some(frame,text)
        showNote()
        // After the number box's own handling, which keeps the value in range.
        text.KeyDown.Add(fun e ->
            match e.KeyCode with
            | Keys.Enter -> answer(); e.Handled <- true; e.SuppressKeyPress <- true
            | Keys.Escape -> pop(); e.Handled <- true; e.SuppressKeyPress <- true
            | _ -> ())
        // Typing again puts the hint back in place of why the last value was refused.
        text.TextChanged.Add(fun _ ->
            match asking with
            | Some(question,Some _) -> asking <- Some(question,None); showNote()
            | _ -> ()
            // Saved as typed or stepped with − and +, as the settings pages save: a value not yet
            // one is left alone, and Enter says why.
            match asking with
            | Some(question,_) when question.live -> question.accept (text.Text.Trim()) |> ignore
            | _ -> ())
        input.ReadOnly <- true
        setPrompt()
        apply()
        askPanel.Visible <- true
        askPanel.BringToFront()
        text.Focus() |> ignore
        text.SelectAll()
        // A colour opens on its picker, as a click on its dot does on the settings pages.
        if form.Visible then colorBox |> Option.iter(fun color -> color.OpenPicker())
    /// On screen and in front, with the keys in the box.
    let present() =
        // Built while hidden, like the switcher's, so it cannot delay the first paint.
        if shadow.IsNone && not SystemInformation.HighContrast then shadow <- Some(new TaskSwitchShadow(form))
        form.Show()
        form.Refresh()
        shadow |> Option.iter(fun item -> item.Show())
        OS().windowFromHwnd(form.Handle).setForegroundOrRestore(true)
        form.Activate()
        input.Focus() |> ignore
    /// Out of sight, the window it was opened over back in front, while a command runs: one
    /// that puts up a box of its own, such as renaming a tab, would lose it to the palette
    /// going away after.
    let conceal() =
        hiding <- true
        shadow |> Option.iter(fun item -> (item :> IDisposable).Dispose())
        shadow <- None
        form.Hide()
    /// Runs a row's command with the palette out of the way; what it leaves to choose brings
    /// the palette back.
    let outcome caption (concealed:bool) (run:unit -> CommandOutcome) =
        if concealed then conceal()
        let back() = if concealed then hiding <- false; present()
        match (try run() with error -> back(); raise error) with
        | Done -> finish None
        | Choose options ->
            back()
            push caption options
        | Ask question ->
            back()
            ask caption question
        | Stay ->
            back()
            reapply()
    /// Enter or a click on a row: switches to the window, or runs the command and shows what it
    /// leaves to choose. A dimmed row does nothing.
    let activate (target:TreeListItem) =
        if not closed && not (isNull target) && not target.Dimmed then
            scoped() |> Array.tryFind(fun (row,_) -> obj.ReferenceEquals(row,target)) |> Option.iter(fun (_,entry) ->
                match choices,page() with
                | (_,_,choose,_)::_,_ ->
                    // Chosen: what was shown stays.
                    previewed <- false
                    previewOptions <- []
                    outcome entry.title true (fun () -> choose entry)
                | [],Some page -> outcome entry.title (page.concealed entry) (fun () -> page.choose entry)
                | [],None -> finish (Some entry.hwnd))
    /// Alt+Enter: the chosen row's shortcut is recorded in a box over the row, as on the
    /// Shortcuts page, with the same refusals; Esc or a click elsewhere leaves it as it was.
    let beginAssign() =
        if assigning.IsNone && not closed then
            let target = list.SelectedItem
            if not (isNull target) then
                scoped() |> Array.tryFind(fun (row,_) -> obj.ReferenceEquals(row,target))
                |> Option.bind(fun (_,entry) ->
                    let assignment = match page() with Some page -> page.shortcut entry | None -> windowShortcut entry
                    assignment |> Option.map(fun assignment -> entry,assignment))
                |> Option.iter(fun (entry,assignment) ->
                    let editor = new SettingsShortcutInput(Font=SettingsUi.bodyFont(),Shortcut=assignment.current,ForeColor=palette.text)
                    // On the chosen row: round its corners the row's highlight shows, not the
                    // list's surface, which showed as dark corners.
                    editor.PlaceOn(if SystemInformation.HighContrast then SystemColors.Highlight else palette.selection)
                    let place() =
                        match list.RowBounds target with
                        | Some bounds ->
                            let margin = Dpi.scale 6
                            editor.Bounds <- Rectangle(bounds.Right-editor.Width-margin,bounds.Top+Dpi.scale 4,editor.Width,bounds.Height-Dpi.scale 8)
                            editor.Visible <- true
                        | None -> editor.Visible <- false
                    assigning <- Some(target,editor)
                    list.Controls.Add(editor)
                    place()
                    editor.BringToFront()
                    editor.SizeChanged.Add(fun _ -> place())
                    editor.Changed.Add(fun _ ->
                        let previous = assignment.current
                        match assignment.usedBy editor.Shortcut with
                        | Some name -> editor.Reject(previous,tr (Strings.Shortcuts.usedBy name))
                        | None when not (assignment.set editor.Shortcut) -> editor.Reject(previous,tr Strings.Shortcuts.inUse)
                        | None ->
                            // Beside the name, where a row's shortcut is shown; the page lists
                            // it afresh from now on.
                            target.Suffix <- SettingsShortcut.text editor.Shortcut
                            list.Invalidate()
                            endAssign())
                    // Esc while recording stops it in the box's own key handling; the key's release
                    // then leaves the box. A refusal's message stays until the next key.
                    editor.KeyUp.Add(fun e -> if e.KeyCode=Keys.Escape && not editor.IsRecording then endAssign())
                    editor.LostFocus.Add(fun _ -> if assigning |> Option.exists(fun (_,current) -> obj.ReferenceEquals(current,editor)) then endAssign())
                    editor.StartRecording())
    let setScope target =
        if target<>scope && List.contains target scopes then
            endAssign()
            scope <- target
            if canSwitchScope && (target=GroupTabs || target=AllTabs) then
                settings |> Option.iter(fun api -> api.setValue("tabSearchScope",box(if target=AllTabs then "All" else "Group")))
            scopeLine.Invalidate()
            footer.Invalidate()
            setPrompt()
            apply()
    /// Tab goes round the scopes; Shift+Tab the other way. Not while a command's choices are up.
    let cycle delta =
        if choices.IsEmpty && scopes.Length>1 then
            let index = List.findIndex ((=) scope) scopes
            setScope scopes.[(index+delta+scopes.Length)%scopes.Length]
    let switchScope() = cycle 1
    let move delta =
        let shown = list.Roots
        if shown.Count>0 then
            let current = shown.IndexOf(list.SelectedItem)
            list.SelectedItem <- shown.[max 0 (min (shown.Count-1) (current+delta))]
    do
        input.HandleCreated.Add(fun _ -> setPrompt())
        input.TextChanged.Add(fun _ -> apply())
        // Tab would otherwise move the focus; here it goes round the scopes.
        input.PreviewKeyDown.Add(fun e -> if e.KeyCode=Keys.Tab then e.IsInputKey <- true)
        // A segment picks its scope; the key cap switches, as the key does. Under a command's
        // choices the line is the way back.
        let pointed (point:Point) =
            let placed,_,key = segments()
            match placed |> List.tryFind(fun (_,bounds) -> bounds.Contains point) with
            | Some(target,_) -> Some target
            | None when key |> Option.exists(fun cap -> cap.Contains point) ->
                let index = List.findIndex ((=) scope) scopes
                Some scopes.[(index+1)%scopes.Length]
            | None -> None
        scopeLine.MouseMove.Add(fun e ->
            let target = if choices.IsEmpty && scopes.Length>1 then pointed e.Location else None
            scopeLine.Cursor <- if target.IsSome || not choices.IsEmpty then Cursors.Hand else Cursors.Default
            if target<>hovered then
                hovered <- target
                scopeLine.Invalidate())
        scopeLine.MouseLeave.Add(fun _ -> if hovered.IsSome then hovered <- None; scopeLine.Invalidate())
        scopeLine.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left then
                if choices.IsEmpty then pointed e.Location |> Option.iter setScope else pop()
                input.Focus() |> ignore)
        input.KeyDown.Add(fun e ->
            let handled =
                match e.KeyCode with
                | Keys.Down -> move 1; true
                | Keys.Up -> move -1; true
                | Keys.PageDown -> move visibleRows; true
                | Keys.PageUp -> move -visibleRows; true
                | Keys.Enter when e.Alt -> beginAssign(); true
                | Keys.Enter when asking.IsSome -> answer(); true
                | Keys.Enter -> activate list.SelectedItem; true
                | Keys.Escape -> (if choices.IsEmpty then finish None else pop()); true
                | Keys.Back when input.Text="" && not choices.IsEmpty -> pop(); true
                | Keys.Tab -> cycle (if e.Shift then -1 else 1); true
                | _ -> false
            if handled then
                e.Handled <- true
                // No beep for Enter and Esc in a single-line box.
                e.SuppressKeyPress <- true)
        // Clicking a row focuses the list; keep the keys working there and go back to typing.
        list.KeyDown.Add(fun e ->
            if e.KeyCode=Keys.Escape then (if choices.IsEmpty then finish None else pop())
            elif e.KeyCode=Keys.Enter && e.Alt then beginAssign())
        list.PreviewKeyDown.Add(fun e -> if e.KeyCode=Keys.Tab then (e.IsInputKey <- true; cycle (if e.Shift then -1 else 1)))
        list.KeyPress.Add(fun e ->
            if not (Char.IsControl e.KeyChar) then
                input.Focus() |> ignore
                input.AppendText(string e.KeyChar)
                e.Handled <- true)
        list.ItemActivated.Add activate
        // A setting's value shown as the choice moves onto it with the keys; Esc puts it back.
        list.SelectionChanged.Add(fun _ ->
            if not selecting && not closed && asking.IsNone && not previewOptions.IsEmpty then
                match list.SelectedItem with
                | null -> ()
                | item ->
                    scoped() |> Array.tryPick(fun (row,entry) -> if obj.ReferenceEquals(row,item) then Some entry.hwnd else None)
                    |> Option.bind(fun hwnd -> List.tryItem (int hwnd-1) previewOptions)
                    |> Option.filter(fun option -> option.preview)
                    |> Option.iter(fun option -> option.run() |> ignore; previewed <- true))
        list.MouseClick.Add(fun e ->
            if e.Button=MouseButtons.Left then list.ItemAt(e.Location) |> Option.iter activate)
        // Going out of the way for a command is no closing.
        // The colour picker's popup takes the keys from the palette without leaving it.
        form.Deactivate.Add(fun _ ->
            let picking = colorBox |> Option.exists(fun color -> color.PickerOpen)
            if not hiding && not picking then finish None)
        apply()

    /// Shown with its search, if one was set, all selected, so typing starts a new one.
    member _.Show() =
        present()
        input.SelectAll()
    /// Closes without switching.
    member _.Close() = finish None
    member _.Ended = ended.Publish
    member _.IsClosed = closed
    /// The typed search; setting it filters as typing does.
    member _.Query with get() = input.Text and set(value:string) = input.Text <- value
    /// The search typed on the scope's rows, under any command's choices: the one to offer again.
    member _.BaseQuery = match List.tryLast choices with Some(_,_,_,query) -> query | None -> input.Text
    member _.Scope = scope
    /// Goes to a scope, as Tab does when it comes round to it.
    member _.SetScope target = setScope target
    /// Goes round the scopes, as Tab does.
    member _.SwitchScope() = switchScope()
    /// The rows the search shows, in order.
    member _.Results = list.Roots |> Seq.map entryOf |> List.ofSeq
    /// What each shown row has beside its name, and at its right end.
    member _.RowNotes = list.Roots |> Seq.map(fun item -> item.Suffix,item.value 1) |> List.ofSeq
    member _.Selected =
        scoped() |> Array.tryFind(fun (item,_) -> obj.ReferenceEquals(item,list.SelectedItem)) |> Option.map snd
    member _.SelectNext() = move 1
    /// Switches to the chosen window, or runs the chosen row, as Enter does.
    member _.Accept() = activate list.SelectedItem
    /// Shows a command's choices, as a command chosen here does, under its name.
    member _.ShowChoices(caption:string, options:CommandChoice list) = push caption options
    /// How many choices deep the box is; 0 on a scope's rows.
    member _.Depth = choices.Length
    /// Whether the windows are among its scopes: the window search, rather than the command palette.
    member _.HasWindows = windows
    /// The line under the box, with the scopes or the way back, takes room.
    member _.ScopeLineShown = scopeLine.Height>0
    /// Lists the rows again, for what came in after they were listed.
    member _.Refresh() = if not closed then reapply()
    member _.SearchBoxUnderlined = searchBox.Underlined
    /// Back from a command's choices, as Esc goes.
    member _.Back() = pop()
    /// Asks for a value, as a command chosen here does, under its name.
    member _.Ask(caption:string, question:CommandQuestion) = ask caption question
    /// Offers what is typed for the value asked, as Enter does.
    member _.Answer() = answer()
    /// What the box says under it: the hint, why a value was refused, or that nothing matches.
    member _.Message = if asking.IsSome then askNote.Text else currentEmpty input.Text
    /// Where the caret is in the search, and how much is selected after it.
    member _.Caret = input.SelectionStart+input.SelectionLength
    /// The value typed in the box asked for, as typed there.
    member _.Answered
        with get() = match askBox with Some(_,text) -> text.Text | None -> ""
        and set(value:string) = askBox |> Option.iter(fun (_,text) -> text.Text <- value)
    /// Whether the box a value is typed in is up, and of which kind.
    member _.AskBox = askBox |> Option.map fst
    /// Starts recording a shortcut for the chosen row, as Alt+Enter does.
    member _.BeginAssign() = beginAssign()
    /// The box recording a shortcut, while one is.
    member _.ShortcutEditor = assigning |> Option.map snd
