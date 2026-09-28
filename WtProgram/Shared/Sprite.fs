namespace Bemo
open System
open System.Drawing

type ISprite =
    abstract member image : Img
    abstract member children : List2<Pt * ISprite>

/// Interactive regions can include transparent pixels around a glyph.
type ISpriteHitTest =
    abstract member containsPoint : Pt -> bool
   
[<AutoOpen>]
module Sprite = 
    type ISprite with
        member this.render : Img =
            let image = this.image.clone()
            let gr = image.graphics
            let draw (childLocation:Pt, child:ISprite) = 
                let image = child.render
                do  gr.DrawImageUnscaled(image.bitmap, childLocation.Point) 
            do  this.children.reverse.iter draw
            image

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
                    | _ -> this.image.containsPoint(pt)
                if contains
                then Some(path)
                else None

        member this.hit pt = this.tryHit(pt, List2([])).def(List2([]))
