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
}

and TabDragInfo = {
    tab: Tab
    tabOffset: Pt
    tabSize: Sz
    tabInfo: TabInfo
    } with
    /// Keep the grabbed point proportional when entering a differently sized tab or DPI.
    member this.offsetIn(size:Sz) =
        let fit offset source target =
            int(Math.Round(float(between (0,max 1 source) offset)*float target/float(max 1 source)))
        Pt(fit this.tabOffset.x this.tabSize.width size.width,fit this.tabOffset.y this.tabSize.height size.height)

and TabStripPlacment = {
    showInside: bool
    bounds: Rect
    }

and TabPart =
    | TabBackground
    | TabIcon
    | TabClose

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
