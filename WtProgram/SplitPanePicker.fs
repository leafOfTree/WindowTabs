namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

module SplitPaneList =
    /// The windows in the order given, the most recently used first, as Switch windows lists
    /// them: the one to put beside the tab is most often among the first.
    let entries (windows:(IntPtr * string * string) list) =
        windows |> List.mapi(fun index (hwnd,title,program) -> { TabSearch.create hwnd title title program with recency=index })

/// The window list for a pane: the tab search's box, over the pane or the group about to split;
/// type to filter, and Enter or a click puts the window there.
type SplitPanePicker() =
    let open' (within:Rectangle) windows choose =
        let entries = SplitPaneList.entries windows
        // Half the screen wide and most of its height, so most windows can be picked without
        // scrolling; the box and scope line above the rows take about two rows.
        let area = Screen.FromRectangle(within).WorkingArea
        let rows = max TabSearchMetrics.visibleRows (area.Height*7/10/Dpi.scale TabSearchMetrics.rowHeight-2)
        let width = max 640 (int(float area.Width/2.0/Dpi.currentFactor()))
        let search = TabSearchForm(entries,[],pick=choose,within=within,prompt=tr Strings.TabMenu.splitSearch,
                                   rows=rows,width=width)
        search.Show()
    interface ISplitPanePicker with
        member _.show(owner,_,windows,choose) = open' (owner.RectangleToScreen(owner.ClientRectangle)) windows choose
        member _.showWithin(within,windows,choose) = open' within windows choose
