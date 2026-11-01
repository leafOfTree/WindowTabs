namespace Bemo
open System
open System.Drawing

type Tab = Tab of IntPtr

and TabInfo = {
    text: string
    iconSmall: Icon
    iconBig: Icon
    preview: unit -> Img
    isRenamed: bool
    /// The splits above this tab's pane, root first, with the side taken at each: the button on
    /// the seam between two pane tabs shows the split that parts them; the text itself stays clean.
    splitTrail: (SplitAxis * Side) list option
    /// Kept at the front of the strip, with a pin where its close button would be.
    isPinned: bool
}

and TabDragInfo = {
    tab: Tab
    tabOffset: Pt
    tabSize: Sz
    tabInfo: TabInfo
    /// Where the tab sits over the window in the drag image, as the strip's alignment would
    /// put a lone tab; the dropped window lands so that the tab stays under the pointer.
    tabShift: int
    /// The strip's alignment, which places a lone tab again over whatever width the window lands at.
    tabAlignment: TabAlignment
    /// From a column at the side: the column the dropped window has its tab in, beside it.
    column: DragColumn option
    } with
    /// Where a lone tab sits over a window of this width.
    member this.shiftFor(windowWidth:int) =
        match this.tabAlignment with
        | TabCenter -> max 0 ((windowWidth-this.tabSize.width)/2)
        | TabRight -> max 0 (windowWidth-this.tabSize.width-60)
        | TabLeft -> 0
    /// Keep the grabbed point proportional when entering a differently sized tab or DPI.
    member this.offsetIn(size:Sz) =
        let fit offset source target =
            int(Math.Round(float(between (0,max 1 source) offset)*float target/float(max 1 source)))
        Pt(fit this.tabOffset.x this.tabSize.width size.width,fit this.tabOffset.y this.tabSize.height size.height)

/// A column of tabs holding one tab, beside the window it lands with, as a group with its tabs
/// at the side puts it outside the window's edge.
and DragColumn = {
    dock: TabDock
    width: int
    height: int
    /// Top, centre or bottom of the window's side.
    alignment: TabAlignment
    /// How far the column runs over the window's frame, so that no seam shows.
    seam: int
    /// Where the tab sits in the column.
    tabInColumn: Pt
    } with
    /// The column's top-left from the window's, for a window of this size.
    member this.columnAt(window:Sz) =
        let spare = max 0 (window.height-this.height)
        let y = match this.alignment with TabLeft -> 0 | TabRight -> spare | TabCenter -> spare/2
        Pt((if this.dock=TabDockLeft then this.seam-this.width else window.width-this.seam),y)
    /// The tab's top-left from the window's.
    member this.tabAt(window:Sz) = this.columnAt(window).add(this.tabInColumn)

/// The image a dragged tab carries: its window at this scale, the tab itself at full size, so
/// the tab stays readable over a window shown small.
and DragImage =
    static member windowScale = 0.5
    /// The window shows a little of what lies under it; the tab is solid, to stay readable.
    static member windowOpacity = 0.85f

and TabStripPlacment = {
    showInside: bool
    bounds: Rect
    }

and TabPart =
    | TabBackground
    | TabIcon
    | TabClose
    /// The button where the two tabs of a split view meet.
    | TabSplit

and TabDirection =
    | TabUp
    | TabDown

and TabAlignment =
    | TabLeft
    | TabCenter
    | TabRight

and TabDock =
    | TabDockTop
    | TabDockBottom
    | TabDockLeft
    | TabDockRight

/// A yes or no kept on a window itself, under a name: it goes with the window, and outlasts
/// WindowTabs starting again.
module WindowFlag =
    let get (key:string) hwnd = WinUserApi.GetProp(hwnd,key)<>IntPtr.Zero
    let set (key:string) hwnd value =
        if value then WinUserApi.SetProp(hwnd,key,IntPtr(1)) |> ignore
        else WinUserApi.RemoveProp(hwnd,key) |> ignore

/// Pinned tabs, kept on the window itself as its tab layout is: one stays pinned in whatever
/// group its window moves to, and once WindowTabs starts again; it goes with the window.
module PinnedTabs =
    let private key = "WindowTabs.Pinned.v1"
    let contains hwnd = WindowFlag.get key hwnd
    let set hwnd value = WindowFlag.set key hwnd value
    /// The pinned ones first, each part in the order it had.
    let ordered (isPinned:'a -> bool) (items:'a list) = List.filter isPinned items @ List.filter (isPinned >> not) items

/// Workspace widths stay in logical pixels across monitor DPI changes.
type GroupTabLayout = {
    dock: TabDock
    columnWidth: int
    normalAlignment: TabAlignment
    insideAlignment: TabAlignment
    iconOnly: bool }

/// How the tabs are drawn. Joined: one bar, the active tab told apart by its fill. Folder: the
/// active tab rises out of the bar like the tab of a paper folder, with curved feet into its
/// neighbours. Pill: the active and hovered tabs are rounded pills set into the bar.
and TabStyle =
    | JoinedTabs
    | FolderTabs
    | PillTabs

and TabAppearanceInfo = {
    tabStyle: TabStyle
    tabHeight: int
    tabMaxWidth: int
    tabOverlap: int
    tabTextColor : Color
    tabNormalBgColor: Color
    tabHighlightBgColor: Color
    tabActiveBgColor: Color
    tabFlashBgColor: Color
    tabBorderColor: Color
    tabHeightOffset : int
    tabIndentFlipped : int
    tabIndentNormal : int
    }
