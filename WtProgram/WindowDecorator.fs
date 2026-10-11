namespace Bemo
open System
open System.Drawing

type WindowDecorator = {
    windowBounds: Rect
    monitorBounds: List2<Rect>
    decoratorHeight : int
    decoratorHeightOffset : int
    decoratorIndentFlipped : int
    decoratorIndentNormal : int
    } with


    member private this.outsideBounds =
        let rect = this.windowBounds
        let indent = this.decoratorIndentNormal
        Rect(
            Pt(rect.x + indent, rect.y - this.decoratorHeight + this.decoratorHeightOffset),
            Sz(rect.width - 2 * indent, this.decoratorHeight)
        )

    /// Over the title bar only the right side, where the caption buttons are, needs the wide indent;
    /// left-aligned tabs then start at the same margin as above the window.
    member this.insideBounds =
        let rect = this.windowBounds
        let left,right = this.decoratorIndentNormal,this.decoratorIndentFlipped
        Rect(
            Pt(rect.x + left, rect.y - 1), // offset by one so it covers edge case #741
            Sz(rect.width - left - right, this.decoratorHeight)
        )

    member this.shouldShowInside = 
        use decoratorOutsideRegion = new Rgn(this.outsideBounds)
        use decoratorInsideRegion = new Rgn(this.insideBounds)
        this.monitorBounds.any <| fun monitorBounds ->
            use monitorRegion = new Rgn(monitorBounds)
            use onMonitorInsideRegion = monitorRegion.intersect(decoratorInsideRegion)
            use onMonitorOutsideRegion = monitorRegion.intersect(decoratorOutsideRegion)
            onMonitorInsideRegion.box.height > onMonitorOutsideRegion.box.height

    /// Prefer the chosen outside edge; an inset overlay keeps snapped windows reachable.
    member this.sidePlacement(dock:TabDock,width:int,?contentHeight:int,?alignment:TabAlignment) : TabStripPlacment =
        let rect = this.windowBounds
        let width = min width (max 1 rect.width)
        let left = dock=TabDockLeft
        let fit (area:Rect) =
            let height = min area.height (max 1 (defaultArg contentHeight area.height))
            let spare = area.height-height
            let offset = match defaultArg alignment TabCenter with TabLeft -> 0 | TabRight -> spare | TabCenter -> spare/2
            Rect(Pt(area.x,area.y+offset),Sz(area.width,height))
        let outside = fit (Rect(Pt((if left then rect.left-width else rect.right),rect.top),Sz(width,rect.height)))
        // Stay on the owner's monitor even when a neighboring screen has outside space.
        let ownerArea =
            match this.monitorBounds.list with
            | [] -> rect
            | areas -> areas |> List.maxBy(fun area ->
                let overlap = rect.intersection area
                int64 overlap.width*int64 overlap.height)
        let fits = ownerArea.completlyContains outside
        // Maximized HWND bounds include an invisible frame beyond the screen edge.
        let overlap = rect.intersection ownerArea
        let visible = if overlap.size.isEmptyArea then rect else overlap
        let inset = min visible.height (Dpi.scale 32)
        let insideWidth = min width (max 1 visible.width)
        let inside = fit (Rect(Pt((if left then visible.left else visible.right-insideWidth),visible.top+inset),
                               Sz(insideWidth,max 1 (visible.height-inset))))
        { showInside=not fits; bounds=if fits then outside else inside }

    member this.bounds : Rect =
        if this.shouldShowInside then this.insideBounds else this.outsideBounds
    

