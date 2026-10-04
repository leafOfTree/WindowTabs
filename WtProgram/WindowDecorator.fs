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

    member this.bounds : Rect =
        if this.shouldShowInside then this.insideBounds else this.outsideBounds
    

