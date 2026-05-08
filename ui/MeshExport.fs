module PointerMk18.Ui.MeshExport

open System
open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Server

[<Emit("new Blob($0, $1)")>]
let private newBlob (parts: obj[]) (opts: obj) : obj = jsNative

[<Emit("URL.createObjectURL($0)")>]
let private urlCreateObjectUrl (blob: obj) : string = jsNative

[<Emit("URL.revokeObjectURL($0)")>]
let private urlRevokeObjectUrl (url: string) : unit = jsNative

[<Emit("window.alert($0)")>]
let private alert (message: string) : unit = jsNative

let mutable private kernelPromise : JS.Promise<Kernel.Wasm.Exports> option = None

let private getKernel () =
    match kernelPromise with
    | Some promise -> promise
    | None ->
        let promise = Kernel.Wasm.load "/kernel/viewer.wasm"
        kernelPromise <- Some promise
        promise

let private exportCompiledState (state: EditorState) (_childId: string) =
    // Visibility state is irrelevant for mesh export — the compiled
    // surfaces are generated from the raw action graph.
    Pipeline.compile state.Doc.Actions state.Doc.Blocks

let private overlayLiveSlotValues (source: SlotTable) (liveValues: float array) (target: SlotTable) =
    let values = Array.copy target.Values

    target.Index
    |> Map.iter (fun slotRef targetSlot ->
        match Map.tryFind slotRef source.Index with
        | Some sourceSlot when sourceSlot < liveValues.Length && targetSlot < values.Length ->
            values.[targetSlot] <- liveValues.[sourceSlot]
        | _ -> ())

    values

let private maxDepthForResolution (resolution: int) =
    let r = max 2 resolution
    max 1 (int (ceil (log (float r) / log 2.0)))

let private halfExtentForMesh (size: float) (resolution: int) =
    max 1e-4 (size * float (max 1 resolution) * 0.5)

let private fallbackName (meshAction: DocAction) =
    meshAction.Name
    |> Option.filter (fun s -> not (String.IsNullOrWhiteSpace s))
    |> Option.defaultValue meshAction.Id

let private buildAsciiStl (name: string) (vertices: float[]) (triangles: int[]) =
    let lines = ResizeArray<string>()

    let inline vertex i =
        let baseIndex = i * 3
        vertices.[baseIndex], vertices.[baseIndex + 1], vertices.[baseIndex + 2]

    let inline subtract (ax, ay, az) (bx, by, bz) =
        ax - bx, ay - by, az - bz

    let inline cross (ax, ay, az) (bx, by, bz) =
        ay * bz - az * by,
        az * bx - ax * bz,
        ax * by - ay * bx

    let inline normalize (x, y, z) =
        let len = sqrt (x * x + y * y + z * z)
        if len <= 1e-9 then 0.0, 0.0, 0.0 else x / len, y / len, z / len

    lines.Add(sprintf "solid %s" name)

    for i in 0 .. 3 .. triangles.Length - 3 do
        let ia = triangles.[i]
        let ib = triangles.[i + 1]
        let ic = triangles.[i + 2]
        let a = vertex ia
        let b = vertex ib
        let c = vertex ic
        let normal =
            cross (subtract b a) (subtract c a)
            |> normalize

        let nx, ny, nz = normal
        let ax, ay, az = a
        let bx, by, bz = b
        let cx, cy, cz = c
        lines.Add(sprintf "  facet normal %.6f %.6f %.6f" nx ny nz)
        lines.Add("    outer loop")
        lines.Add(sprintf "      vertex %.6f %.6f %.6f" ax ay az)
        lines.Add(sprintf "      vertex %.6f %.6f %.6f" bx by bz)
        lines.Add(sprintf "      vertex %.6f %.6f %.6f" cx cy cz)
        lines.Add("    endloop")
        lines.Add("  endfacet")

    lines.Add(sprintf "endsolid %s" name)
    String.concat "\n" lines

let downloadMesh (meshAction: DocAction) : unit =
    promise {
        try
            match meshAction.Kind with
            | Mesh(Some childId, size, resolution) ->
                let state = AppStore.store.State
                let compiled = exportCompiledState state childId

                match compiled.Surfaces |> List.tryFind (fun surface -> surface.ActionId = childId) with
                | None ->
                    failwithf "Mesh child '%s' does not produce a field surface." childId
                | Some surface ->
                    let values =
                        overlayLiveSlotValues state.Compiled.Slots state.SlotValues compiled.Slots

                    match Kernel.FieldToIr.build [ surface ] values with
                    | None ->
                        failwith "Failed to build kernel IR for mesh export."
                    | Some ir ->
                        let! kernel = getKernel ()
                        let uploadStatus = Kernel.Wasm.uploadIr kernel ir
                        if uploadStatus <> 0 then
                            failwithf "Kernel IR upload failed (%d)." uploadStatus

                        let meshStatus =
                            kernel.mesh_build(
                                halfExtentForMesh size resolution,
                                maxDepthForResolution resolution
                            )

                        if meshStatus <> 0 then
                            failwithf "Kernel mesh build failed (%d)." meshStatus

                        let vertices = Kernel.Wasm.meshVertices kernel
                        let triangles = Kernel.Wasm.meshTriangles kernel

                        if vertices.Length = 0 || triangles.Length = 0 then
                            failwith "Kernel mesher returned an empty mesh."

                        let baseName = fallbackName meshAction
                        let stl = buildAsciiStl baseName vertices triangles
                        let blob = newBlob [| stl :> obj |] {| ``type`` = "model/stl" |}
                        let url = urlCreateObjectUrl blob
                        let link = document.createElement "a" :?> HTMLAnchorElement
                        link.href <- url
                        link?download <- sprintf "%s.stl" baseName
                        link.click ()
                        urlRevokeObjectUrl url
            | Mesh(None, _, _) ->
                alert "Set the mesh child first."
            | _ ->
                failwith "downloadMesh called for a non-mesh action."
        with error ->
            console.error("Mesh export failed", error)
            alert (sprintf "Mesh export failed: %s" error.Message)
    }
    |> ignore
