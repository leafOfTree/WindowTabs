namespace Bemo
open System

type HideTabsOnInactiveGroupPlugin() =
    let Cell = CellScope()
    let isDragging = Cell.create(false)

    member this.group = Services.get<WindowGroup>()

    member this.hasGroup = Services.has<WindowGroup>()

    member this.tabStrip = Services.get<TabStrip>()

    member this.shouldShowCompact = 
        this.group.isForeground.value.not &&
        this.group.isMouseOver.value.not &&
        isDragging.value.not &&
        Services.settings.getValue("hideInactiveTabs").cast<bool>()

    member private this.onShowCompactChanged() =
        if this.shouldShowCompact then
            this.tabStrip.alpha <- byte(0x60)
        else
            this.tabStrip.alpha <- byte(0xFF)

    interface IDragDropNotification with
        member this.dragBegin() = this.group.invokeAsync <| fun() ->
            isDragging.set(true)

        member this.dragEnd() = this.group.invokeAsync <| fun() ->
            isDragging.set(false)

    interface IPlugin with
        member this.init() =
            Services.dragDrop.registerNotification(this :> IDragDropNotification)
            Services.settings.notifyValue "hideInactiveTabs" <| fun(_) ->
                if this.hasGroup then
                    this.group.invokeAsync <| fun() ->
                        this.onShowCompactChanged()
            this.group.zorder.changed.Add <| fun() ->
                this.onShowCompactChanged()
            this.group.isForeground.changed.Add <| fun() ->
                this.onShowCompactChanged()
            this.group.isMouseOver.changed.Add <| fun() ->
                this.onShowCompactChanged()
            Cell.listen <| fun() ->
                isDragging.value.ignore
                this.onShowCompactChanged()