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


    member private this.indent(isCentered) = if isCentered then this.decoratorIndentFlipped else this.decoratorIndentNormal

    member private this.outsideBounds =
        let rect = this.windowBounds
        let indent = this.indent(false)
        Rect(
            Pt(rect.x + indent, rect.y - this.decoratorHeight + this.decoratorHeightOffset),
            Sz(rect.width - 2 * indent, this.decoratorHeight)
        )

    member this.insideBounds = 
        let rect = this.windowBounds
        let indent = this.indent(true)
        Rect(
            Pt(rect.x + indent, rect.y - 1), // offset by one so it covers edge case #741
            Sz(rect.width - 2 * indent, this.decoratorHeight)
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
    

