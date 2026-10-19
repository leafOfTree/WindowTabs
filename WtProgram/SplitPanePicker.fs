namespace Bemo
open System
open System.Windows.Forms

/// The empty pane's window list: the tab search's box, over the pane, sorted by app and then
/// title with a divider between apps; type to filter, and Enter or a click fills the pane.
type SplitPanePicker() =
    interface ISplitPanePicker with
        member _.show(owner,_,windows,choose) =
            let key (text:string) = text.ToLowerInvariant()
            let sorted =
                windows
                |> List.map(fun (hwnd,title,program) -> TabSearch.create hwnd title title program)
                // Windows whose program cannot be read, elevated system tools such as Task Manager
                // or hosts of Store apps, go last: they are seldom the ones to put beside another.
                |> List.sortBy(fun entry -> entry.program="",key entry.program,key entry.title)
            let apps = sorted |> List.map(fun entry -> key entry.program) |> List.distinct
            let entries = sorted |> List.mapi(fun index entry ->
                            { entry with group=List.findIndex ((=) (key entry.program)) apps; recency=index })
            // Half the screen wide and most of its height, so most windows can be picked without
            // scrolling; the box and scope line above the rows take about two rows.
            let within = owner.RectangleToScreen(owner.ClientRectangle)
            let area = Screen.FromRectangle(within).WorkingArea
            let rows = max 9 (area.Height*7/10/Dpi.scale 44-2)
            let width = max 640 (int(float area.Width/2.0/Dpi.currentFactor()))
            let search = TabSearchForm(entries,[],pick=choose,within=within,prompt=tr Strings.TabMenu.splitSearch,
                                       rows=rows,width=width)
            search.Show()
