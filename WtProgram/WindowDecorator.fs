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
    member this.sidePlacement(dock:TabDock,width:int) : TabStripPlacment =
        let rect = this.windowBounds
        let width = min width (max 1 rect.width)
        let left = dock=TabDockLeft
        let outside = Rect(Pt((if left then rect.left-width else rect.right),rect.top),Sz(width,rect.height))
        let fits = this.monitorBounds.any(fun area -> area.completlyContains outside)
        let inset = min rect.height (Dpi.scale 32)
        let inside = Rect(Pt((if left then rect.left else rect.right-width),rect.top+inset),Sz(width,max 1 (rect.height-inset)))
        { showInside=not fits; bounds=if fits then outside else inside }

    member this.bounds : Rect =
        if this.shouldShowInside then this.insideBounds else this.outsideBounds
    

