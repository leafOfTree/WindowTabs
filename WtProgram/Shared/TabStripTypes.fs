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
    tabInfo: TabInfo
    }

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
