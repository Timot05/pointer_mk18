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
    | Sketch of origin: ActionId option * sketch: ActionSketch
    | FromSketch of child: ActionId option * closed: bool * flip: bool * selection: FromSketchSelection
    | Thicken of child: ActionId option * amount: float
    | Shell of child: ActionId option * thickness: float
    | Mesh of child: ActionId option * size: float * resolution: int

type DisplaySettings =
    { Enabled: bool
      Color: float array  // [r, g, b] normalized 0-1
      Opacity: float
      IsoValue: float }

module DisplaySettings =
    let defaults =
        { Enabled = false
          Color = [| 0.522; 0.682; 0.784 |]  // #85AEC8
          Opacity = 0.9
          IsoValue = 0.0 }

type FieldSliceSettings =
    { Enabled: bool
      Plane: string  // "X" | "Y" | "Z"
      Offset: float
      Extent: float }

module FieldSliceSettings =
    let defaults =
        { Enabled = false
          Plane = "Z"
          Offset = 0.0
          Extent = 0.5 }

type DocAction =
    { Id: ActionId
      Name: string option
      Kind: ActionKind
      Visible: bool
      Display: DisplaySettings option
      FieldSlice: FieldSliceSettings option }

type Document ={ 
    Name: string
    Actions: DocAction list
    SelectedId: string option 
}

module Document =

    let select (id: string) (doc: Document) : Document =
        { doc with SelectedId = Some id }

    let addAction (action: DocAction) (doc: Document) : Document =
        { doc with Actions = doc.Actions @ [ action ]; SelectedId = Some action.Id }

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

    let toggleDisplay (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                        { a with Display = Some { d with Enabled = not d.Enabled } }) }

    let patchDisplay (id: string) (key: string) (value: System.Text.Json.JsonElement) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let d = a.Display |> Option.defaultValue DisplaySettings.defaults
                        let d' =
                            match key with
                            | "color" ->
                                let arr = value.EnumerateArray() |> Seq.map (fun e -> e.GetDouble()) |> Seq.toArray
                                { d with Color = arr }
                            | "opacity" -> { d with Opacity = value.GetDouble() }
                            | "isoValue" -> { d with IsoValue = value.GetDouble() }
                            | _ -> d
                        { a with Display = Some d' }) }

    let toggleFieldSlice (id: string) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                        { a with FieldSlice = Some { fs with Enabled = not fs.Enabled } }) }

    let patchFieldSlice (id: string) (key: string) (value: System.Text.Json.JsonElement) (doc: Document) : Document =
        { doc with
            Actions =
                doc.Actions
                |> List.map (fun a ->
                    if a.Id <> id then a
                    else
                        let fs = a.FieldSlice |> Option.defaultValue FieldSliceSettings.defaults
                        let fs' =
                            match key with
                            | "plane" -> { fs with Plane = value.GetString() }
                            | "offset" -> { fs with Offset = value.GetDouble() }
                            | _ -> fs
                        { a with FieldSlice = Some fs' }) }

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
                            | Sketch(origin, s) ->
                                Sketch(
                                    (if key = "origin" then optStr value else origin),
                                    s)
                            | FromSketch(c, closed, flip, sel) ->
                                FromSketch(
                                    (if key = "child" then optStr value else c),
                                    (if key = "closed" then value.GetBoolean() else closed),
                                    (if key = "flip" then value.GetBoolean() else flip),
                                    sel)
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
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "cyl1"
                Name = Some "cylinder"
                Kind = Cylinder(radius = 10.0, height = 40.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "sph1"
                Name = Some "sphere"
                Kind = Sphere(radius = 8.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "sub1"
                Name = Some "subtract"
                Kind = Subtract(a = Some "cyl1", b = Some "sph1", radius = 0.0)
                Visible = true
                Display = None
                FieldSlice = None }
              { Id = "sketch1"
                Name = Some "square"
                Kind = Sketch(
                    origin = Some "origin",
                    sketch =
                        { Entities =
                            [ REPoint("p_bl",  0.0,  0.0)
                              REPoint("p_br", 10.0,  0.0)
                              REPoint("p_tr", 10.0, 10.0)
                              REPoint("p_tl",  0.0, 10.0)
                              RELine("l_bottom", "p_bl", "p_br")
                              RELine("l_right",  "p_br", "p_tr")
                              RELine("l_top",    "p_tr", "p_tl")
                              RELine("l_left",   "p_tl", "p_bl") ]
                          Constraints =
                            [ Fixed("p_bl", 0.0, 0.0)
                              Horizontal("p_bl", "p_br")
                              Horizontal("p_tl", "p_tr")
                              Vertical("p_bl", "p_tl")
                              Vertical("p_br", "p_tr")
                              Distance("p_bl", "p_br", 10.0, None)
                              Distance("p_bl", "p_tl", 10.0, None) ] })
                Visible = true
                Display = None
                FieldSlice = None } ] }
