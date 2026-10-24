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

/// Pinned tabs by window: one stays pinned in whatever group its window moves to. Written and
/// read on every group's thread.
module PinnedTabs =
    let private pinned = Collections.Concurrent.ConcurrentDictionary<IntPtr,bool>()
    let contains hwnd = pinned.ContainsKey hwnd
    let set hwnd value = if value then pinned.[hwnd] <- true else pinned.TryRemove(hwnd) |> ignore
    /// The pinned ones first, each part in the order it had.
    let ordered (isPinned:'a -> bool) (items:'a list) = List.filter isPinned items @ List.filter (isPinned >> not) items

/// Workspace widths stay in logical pixels across monitor DPI changes.
type GroupTabLayout = {
    dock: TabDock
    columnWidth: int
    normalAlignment: TabAlignment
    insideAlignment: TabAlignment }

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
