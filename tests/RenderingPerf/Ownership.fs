module RenderingOwnership

open System
open System.Drawing
open Bemo

let verify() =
    let check condition message = if not condition then failwith message
    let disposed (bitmap:Bitmap) =
        try bitmap.GetPixel(0,0) |> ignore; false
        with :? ArgumentException -> true
    let allocated = ResizeArray<Bitmap>()
    let sprite color children =
        { new ISprite with
            member _.image =
                let bitmap = new Bitmap(4,4)
                allocated.Add(bitmap)
                use graphics = Graphics.FromImage(bitmap)
                graphics.Clear(color)
                Img(bitmap)
            member _.children = children() }
    let child = sprite Color.Red (fun () -> List2())
    let parent = sprite Color.Blue (fun () -> List2([Pt(0,0),child]))
    use rendered = parent.render.bitmap
    check (rendered.GetPixel(0,0).ToArgb()=Color.Red.ToArgb()) "Child compositing changed"
    check (not(disposed rendered)) "Renderer disposed its returned image"
    check (allocated.Count=2 && disposed allocated.[1]) "Renderer retained a temporary child bitmap"
    allocated.Clear()
    check (not((child.hit(Pt(0,0))).isEmpty)) "Opaque hit lost"
    check (allocated.Count=1 && disposed allocated.[0]) "Hit testing retained its temporary bitmap"
    allocated.Clear()
    check ((child.hit(Pt(-1,0))).isEmpty) "Out-of-bounds hit accepted"
    check (allocated.Count=1 && disposed allocated.[0]) "Miss retained its temporary bitmap"
    allocated.Clear()
    let failure = InvalidOperationException("injected child failure")
    let broken = sprite Color.Blue (fun () -> raise failure)
    let outer = sprite Color.White (fun () -> List2([Pt(0,0),broken]))
    let mutable caught = false
    try outer.render |> ignore
    with ex when Object.ReferenceEquals(ex,failure) -> caught <- true
    check caught "Render failure was swallowed"
    check (allocated.Count=2 && (allocated |> Seq.forall disposed)) "Failed render retained a bitmap"
    printfn "PASS: sprite compositing, returned image ownership, hit/miss cleanup and nested failure cleanup."
