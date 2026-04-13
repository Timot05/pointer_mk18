namespace Server

open System.Text.Json.Serialization

// ---------------------------------------------------------------------------
// Domain types — the document model that the frontend renders
// ---------------------------------------------------------------------------

type ActionId = string

type ActionKind =
    | Origin
    | Cylinder of radius: float * height: float
    | Sphere of radius: float
    | Box of width: float * height: float * depth: float
    | HalfPlane of axis: string * offset: float * flip: bool
    | Translate of child: ActionId option * x: float * y: float * z: float
    | Rotate of child: ActionId option * ax: float * ay: float * az: float * angle: float
    | Move of child: ActionId option * frame: ActionId option
    | Union of a: ActionId option * b: ActionId option * radius: float
    | Subtract of a: ActionId option * b: ActionId option * radius: float
    | Intersect of a: ActionId option * b: ActionId option * radius: float
    | Sketch
    | FromSketch of child: ActionId option * closed: bool * flip: bool
    | Thicken of child: ActionId option * amount: float
    | Shell of child: ActionId option * thickness: float
    | Mesh of child: ActionId option * size: float * resolution: int

type DocAction =
    { Id: ActionId
      Name: string option
      Kind: ActionKind
      Visible: bool }

type Document ={ 
    Name: string
    Actions: DocAction list
    SelectedId: string option 
}

module Document =

    let select (id: string) (doc: Document) : Document =
        { doc with SelectedId = Some id }

    let addAction (action: DocAction) (doc: Document) : Document =
        let insertIdx =
            match doc.SelectedId with
            | Some selId ->
                doc.Actions
                |> List.tryFindIndex (fun a -> a.Id = selId)
                |> Option.map (fun i -> i + 1)
                |> Option.defaultValue doc.Actions.Length
            | None -> doc.Actions.Length

        let before = doc.Actions |> List.take insertIdx
        let after = doc.Actions |> List.skip insertIdx
        { doc with Actions = before @ [ action ] @ after; SelectedId = Some action.Id }

    let updateAction (id: string) (updated: DocAction) (doc: Document) : Document =
        { doc with Actions = doc.Actions |> List.map (fun a -> if a.Id = id then updated else a) }

    let removeAction (id: string) (doc: Document) : Document =
        { doc with
            Actions = doc.Actions |> List.filter (fun a -> a.Id <> id)
            SelectedId = if doc.SelectedId = Some id then None else doc.SelectedId }

    let toggleVisible (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a -> if a.Id = id then { a with Visible = not a.Visible } else a) }

    let reorder (ids: string list) (doc: Document) : Document =
        let lookup = doc.Actions |> List.map (fun a -> a.Id, a) |> Map.ofList
        { doc with Actions = ids |> List.choose (fun id -> Map.tryFind id lookup) }

    let private optStr (value: System.Text.Json.JsonElement) =
        let s = value.GetString()
        if System.String.IsNullOrEmpty(s) then None else Some s

    let patchParam (id: string) (key: string) (value: System.Text.Json.JsonElement) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let kind =
                            match a.Kind with
                            | Cylinder(r, h) ->
                                Cylinder(
                                    (if key = "radius" then value.GetDouble() else r),
                                    (if key = "height" then value.GetDouble() else h))
                            | Sphere r ->
                                Sphere(if key = "radius" then value.GetDouble() else r)
                            | Box(w, h, d) ->
                                Box(
                                    (if key = "width" then value.GetDouble() else w),
                                    (if key = "height" then value.GetDouble() else h),
                                    (if key = "depth" then value.GetDouble() else d))
                            | Translate(c, x, y, z) ->
                                Translate(
                                    (if key = "child" then optStr value else c),
                                    (if key = "x" then value.GetDouble() else x),
                                    (if key = "y" then value.GetDouble() else y),
                                    (if key = "z" then value.GetDouble() else z))
                            | Rotate(c, ax, ay, az, ang) ->
                                Rotate(
                                    (if key = "child" then optStr value else c),
                                    (if key = "ax" then value.GetDouble() else ax),
                                    (if key = "ay" then value.GetDouble() else ay),
                                    (if key = "az" then value.GetDouble() else az),
                                    (if key = "angle" then value.GetDouble() else ang))
                            | HalfPlane(ax, off, fl) ->
                                HalfPlane(
                                    (if key = "axis" then value.GetString() else ax),
                                    (if key = "offset" then value.GetDouble() else off),
                                    (if key = "flip" then value.GetBoolean() else fl))
                            | Move(c, f) ->
                                Move(
                                    (if key = "child" then optStr value else c),
                                    (if key = "frame" then optStr value else f))
                            | Union(a, b, r) ->
                                Union(
                                    (if key = "a" then optStr value else a),
                                    (if key = "b" then optStr value else b),
                                    (if key = "radius" then value.GetDouble() else r))
                            | Subtract(a, b, r) ->
                                Subtract(
                                    (if key = "a" then optStr value else a),
                                    (if key = "b" then optStr value else b),
                                    (if key = "radius" then value.GetDouble() else r))
                            | Intersect(a, b, r) ->
                                Intersect(
                                    (if key = "a" then optStr value else a),
                                    (if key = "b" then optStr value else b),
                                    (if key = "radius" then value.GetDouble() else r))
                            | FromSketch(c, closed, flip) ->
                                FromSketch(
                                    (if key = "child" then optStr value else c),
                                    (if key = "closed" then value.GetBoolean() else closed),
                                    (if key = "flip" then value.GetBoolean() else flip))
                            | Thicken(c, amt) ->
                                Thicken(
                                    (if key = "child" then optStr value else c),
                                    (if key = "amount" then value.GetDouble() else amt))
                            | Shell(c, t) ->
                                Shell(
                                    (if key = "child" then optStr value else c),
                                    (if key = "thickness" then value.GetDouble() else t))
                            | Mesh(c, s, res) ->
                                Mesh(
                                    (if key = "child" then optStr value else c),
                                    (if key = "size" then value.GetDouble() else s),
                                    (if key = "resolution" then value.GetInt32() else res))
                            | other -> other
                        { a with Kind = kind }) }

    let defaultDocument () : Document =
        { Name = "untitled"
          SelectedId = Some "origin"
          Actions =
            [ { Id = "origin"
                Name = Some "origin"
                Kind = Origin
                Visible = true }
              { Id = "cyl1"
                Name = Some "cylinder"
                Kind = Cylinder(radius = 10.0, height = 40.0)
                Visible = true }
              { Id = "sph1"
                Name = Some "sphere"
                Kind = Sphere(radius = 8.0)
                Visible = true }
              { Id = "sub1"
                Name = Some "subtract"
                Kind = Subtract(a = Some "cyl1", b = Some "sph1", radius = 0.0)
                Visible = true } ] }
