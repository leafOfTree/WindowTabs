namespace Bemo
open System
open System.Drawing

type ISprite =
    /// Returns a fresh image owned by the caller, including for hit testing.
    abstract member image : Img
    abstract member children : List2<Pt * ISprite>

/// Interactive regions can include transparent pixels around a glyph.
type ISpriteHitTest =
    abstract member containsPoint : Pt -> bool
   
[<AutoOpen>]
module Sprite = 
    type ISprite with
        member this.render : Img =
            let image = this.image
            try
                use gr = image.graphics
                let draw (childLocation:Pt, child:ISprite) =
                    use bitmap = child.render.bitmap
                    gr.DrawImageUnscaled(bitmap, childLocation.Point)
                this.children.reverse.iter draw
                image
            with _ ->
                image.bitmap.Dispose()
                reraise()

        member this.tryHit(pt:Pt, path:List2<ISprite>) =
            let path = path.prepend(this)
            let hitPath = this.children.tryPick <| fun (location, child) ->
                let pt = pt.sub(location)
                child.tryHit(pt, path)
            match hitPath with
            | Some(path) -> Some(path)
            | None ->
                let contains =
                    match this with
                    | :? ISpriteHitTest as target -> target.containsPoint(pt)
                    | _ ->
                        let image = this.image
                        try image.containsPoint(pt)
                        finally image.bitmap.Dispose()
                if contains
                then Some(path)
                else None

        member this.hit pt = this.tryHit(pt, List2([])).def(List2([]))
