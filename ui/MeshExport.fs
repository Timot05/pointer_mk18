module PointerMk18.Ui.MeshExport

open System.Text
open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server
open Server.Lang
open Kernel

[<Emit("new Blob($0, $1)")>]
let private newBlob (parts: obj[]) (opts: obj) : obj = jsNative

[<Emit("URL.createObjectURL($0)")>]
let private createObjectUrl (blob: obj) : string = jsNative

[<Emit("URL.revokeObjectURL($0)")>]
let private revokeObjectUrl (url: string) : unit = jsNative

[<Emit("$0.download = $1")>]
let private setDownloadName (el: HTMLAnchorElement) (filename: string) : unit = jsNative

let private vertex (vertices: float[]) (idx: int) =
    let i = idx * 3
    vertices.[i], vertices.[i + 1], vertices.[i + 2]

let private normal
        (ax: float, ay: float, az: float)
        (bx: float, by: float, bz: float)
        (cx: float, cy: float, cz: float) =
    let ux, uy, uz = bx - ax, by - ay, bz - az
    let vx, vy, vz = cx - ax, cy - ay, cz - az
    let nx = uy * vz - uz * vy
    let ny = uz * vx - ux * vz
    let nz = ux * vy - uy * vx
    let len = sqrt (nx * nx + ny * ny + nz * nz)
    if len <= 1.0e-12 then 0.0, 0.0, 0.0 else nx / len, ny / len, nz / len

let private buildAsciiStl (vertices: float[]) (triangles: int[]) =
    let sb = StringBuilder()
    sb.AppendLine "solid dekal" |> ignore
    for i in 0 .. 3 .. triangles.Length - 1 do
        let a = vertex vertices triangles.[i]
        let b = vertex vertices triangles.[i + 1]
        let c = vertex vertices triangles.[i + 2]
        let nx, ny, nz = normal a b c
        let ax, ay, az = a
        let bx, by, bz = b
        let cx, cy, cz = c
        sb.AppendLine(sprintf "  facet normal %.9g %.9g %.9g" nx ny nz) |> ignore
        sb.AppendLine "    outer loop" |> ignore
        sb.AppendLine(sprintf "      vertex %.9g %.9g %.9g" ax ay az) |> ignore
        sb.AppendLine(sprintf "      vertex %.9g %.9g %.9g" bx by bz) |> ignore
        sb.AppendLine(sprintf "      vertex %.9g %.9g %.9g" cx cy cz) |> ignore
        sb.AppendLine "    endloop" |> ignore
        sb.AppendLine "  endfacet" |> ignore
    sb.AppendLine "endsolid dekal" |> ignore
    sb.ToString()

let private downloadText (filename: string) (contents: string) =
    let blob = newBlob ([| contents :> obj |]) (createObj [ "type" ==> "model/stl;charset=utf-8" ])
    let url = createObjectUrl blob
    let a = document.createElement "a" :?> HTMLAnchorElement
    a.href <- url
    setDownloadName a filename
    document.body.appendChild a |> ignore
    a.click ()
    a.remove ()
    revokeObjectUrl url

/// Run `mesh_build` against an already-encoded IR blob and download the
/// result as ASCII STL. Used by `downloadBlockStl` (per-block right-click
/// export).
let private runMeshExport (filename: string) (bytes: obj) : JS.Promise<unit> =
    promise {
        let! wasm = Wasm.load "/kernel/viewer.wasm"
        let uploadCode = Wasm.uploadIr wasm bytes
        if uploadCode <> 0 then
            window.alert (sprintf "Kernel rejected the current scene (IR upload code %d)." uploadCode)
        else
            // `mesh_build_auto` interval-prunes (and Lipschitz-prunes) a
            // large root box to find a tight AABB around the surface, then
            // meshes it — so the octree depth budget goes to the surface
            // instead of empty space. Falls back to the fixed-cube
            // `mesh_build(20, 7)` if auto can't find a surface in its
            // search box (code 5) or finds one but produces no triangles
            // (code 4 — happens when the field's Lipschitz estimate at
            // the centre is too pessimistic and bounds end up too tight).
            // Depth 10 is the kernel's max (`web_main.zig` rejects >10).
            // At this depth the octree resolves up to 1024 leaves per
            // axis — fine enough for visibly smooth surfaces on
            // notebook-scale scenes. Export only runs on right-click so
            // a few seconds is acceptable.
            let meshCode =
                let auto = wasm.mesh_build_auto 10
                if auto = 4 || auto = 5 then wasm.mesh_build (20.0, 10) else auto
            if meshCode <> 0 then
                window.alert (sprintf "Mesh export failed (code %d)." meshCode)
            else
                let vertices = Wasm.meshVertices wasm
                let triangles = Wasm.meshTriangles wasm
                if vertices.Length = 0 || triangles.Length = 0 then
                    window.alert "Mesh export produced no triangles."
                else
                    downloadText filename (buildAsciiStl vertices triangles)
    }

/// Re-serialize the current notebook IR with `block`'s field expression as
/// the scene root, then run the mesh-build pipeline. Returns silently if
/// the block isn't a Field producer (no entry in `NotebookFieldExprByBlock`).
let downloadBlockStl (blockId: Notebook.BlockId) : unit =
    promise {
        let state = AppStore.store.State
        match state.NotebookIr, Map.tryFind blockId state.NotebookFieldExprByBlock with
        | Some ir, Some expr ->
            let name =
                state.Doc.Blocks
                |> List.tryFind (fun b -> b.Id = blockId)
                |> Option.map (fun b -> b.Name)
                |> Option.defaultValue (sprintf "block-%d" blockId)
            let bytes = MathIrCodec.serialize ir expr []
            do! runMeshExport (sprintf "%s.stl" name) bytes
        | _ ->
            window.alert "This block doesn't produce a field — nothing to extract."
    }
    |> ignore
