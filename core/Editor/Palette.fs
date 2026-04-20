namespace Server

// ---------------------------------------------------------------------------
// Command palette state machine
// ---------------------------------------------------------------------------

/// A single scalar field within a scalars step.
type ScalarDef =
    { Key: string
      Label: string
      Default: float }

/// Each step in the palette wizard is either picking a ref or adjusting scalars.
type PaletteStep =
    | RefStep of key: string * label: string * accepts: FieldType list
    | ScalarsStep of label: string * fields: ScalarDef list

// ── Types sent to the frontend ───────────────────────────────────────

type PaletteItem =
    { Id: string
      Label: string
      Kind: string }

type PaletteChip =
    { Label: string
      Value: string }

type PaletteScalarField =
    { Key: string
      Label: string
      Value: float }

type PaletteState =
    { IsOpen: bool
      Mode: string // "closed" | "command" | "ref" | "scalars" | "done"
      PickedKind: string option
      Chips: PaletteChip list
      Prompt: string
      Items: PaletteItem list
      ScalarFields: PaletteScalarField list
      HintBar: string list }

// ── Session (server-side only) ───────────────────────────────────────

type PaletteSession =
    { PickedKind: string option
      Steps: PaletteStep list
      StepIndex: int
      Values: Map<string, string>
      Query: string }

module Palette =

    // ── Step definitions per action kind ──────────────────────────────

    /// Build a dummy ActionKind from a kind name to look up accepted input types.
    let private dummyKind (kind: string) : ActionKind option =
        match kind with
        | "Translate" -> Some (Translate(None, 0.0, 0.0, 0.0))
        | "Rotate" -> Some (Rotate(None, 0.0, 0.0, 1.0, 0.0))
        | "Move" -> Some (ActionKind.Move(None, None))
        | "Union" -> Some (Union(None, None, 0.0))
        | "Subtract" -> Some (Subtract(None, None, 0.0))
        | "Intersect" -> Some (Intersect(None, None, 0.0))
        | "Sketch" -> Some (ActionKind.Sketch(None, SketchPlane.defaults, ActionSketch.empty))
        | "FromSketch" -> Some (FromSketch(None, false, FromSketchSelection.defaults))
        | "Thicken" -> Some (Thicken(None, 0.0))
        | "Shell" -> Some (Shell(None, 0.0))
        | "Mesh" -> Some (Mesh(None, 0.0, 0))
        | _ -> None

    let private stepsFor (kind: string) : PaletteStep list =
        let accepted =
            dummyKind kind
            |> Option.map TypeCheck.acceptedInputs
            |> Option.defaultValue Map.empty
        let ref' key label =
            let types = accepted |> Map.tryFind key |> Option.defaultValue []
            RefStep(key, label, types)
        let scalars label fields = ScalarsStep(label, fields)
        let s key label def = { Key = key; Label = label; Default = def }
        match kind with
        | "Sphere" ->
            [ scalars "dimensions" [ s "radius" "radius" 8.0 ] ]
        | "Cylinder" ->
            [ scalars "dimensions" [ s "radius" "radius" 5.0; s "height" "height" 20.0 ] ]
        | "Box" ->
            [ scalars "dimensions" [ s "width" "width" 10.0; s "height" "height" 10.0; s "depth" "depth" 10.0 ] ]
        | "HalfPlane" ->
            [ scalars "offset" [ s "offset" "offset" 0.0 ] ]
        | "Translate" ->
            [ ref' "child" "from"
              scalars "offset" [ s "x" "x" 0.0; s "y" "y" 0.0; s "z" "z" 0.0 ] ]
        | "Rotate" ->
            [ ref' "child" "from"
              scalars "axis" [ s "ax" "ax" 0.0; s "ay" "ay" 0.0; s "az" "az" 1.0 ]
              scalars "rotation" [ s "angle" "angle" 0.0 ] ]
        | "Move" ->
            [ ref' "child" "from"; ref' "frame" "to frame" ]
        | "Sketch" ->
            [ ref' "origin" "on frame" ]
        | "FromSketch" ->
            [ ref' "child" "sketch" ]
        | "Union" | "Subtract" | "Intersect" ->
            [ ref' "a" "tool"; ref' "b" "target"
              scalars "blend" [ s "radius" "blend" 0.0 ] ]
        | "Thicken" ->
            [ ref' "child" "from"
              scalars "amount" [ s "amount" "amount" 2.0 ] ]
        | "Shell" ->
            [ ref' "child" "from"
              scalars "thickness" [ s "thickness" "thickness" 1.0 ] ]
        | "Mesh" ->
            [ ref' "child" "from"
              scalars "mesh" [ s "size" "size" 0.2; s "resolution" "res" 96.0 ] ]
        | _ -> []

    let private templateLabels =
        [ "Sphere"; "Cylinder"; "Box"; "HalfPlane"; "Translate"; "Rotate"; "Move"
          "Union"; "Subtract"; "Intersect"; "Sketch"; "FromSketch"
          "Thicken"; "Shell"; "Mesh" ]

    let private fuzzyMatch (query: string) (text: string) =
        let q = query.ToLowerInvariant()
        let t = text.ToLowerInvariant()
        let mutable qi = 0
        for ti in 0 .. t.Length - 1 do
            if qi < q.Length && t.[ti] = q.[qi] then
                qi <- qi + 1
        qi = q.Length

    let private filterTemplates (query: string) : PaletteItem list =
        let labels =
            if System.String.IsNullOrEmpty(query) then templateLabels
            else templateLabels |> List.filter (fuzzyMatch query)
        labels |> List.map (fun l -> { Id = l; Label = l; Kind = l })

    let private filterActions (query: string) (accepts: FieldType list) (typeMap: Map<ActionId, FieldType>) (doc: Document) : PaletteItem list =
        doc.Actions
        |> List.filter (fun a ->
            (match Map.tryFind a.Id typeMap with
             | Some t -> List.contains t accepts
             | None -> false) &&
            (System.String.IsNullOrEmpty(query) ||
             fuzzyMatch query (a.Name |> Option.defaultValue (a.Kind.ToString()))))
        |> List.map (fun a ->
            let label = a.Name |> Option.defaultValue (a.Kind.ToString().Split('(').[0])
            { Id = a.Id; Label = label; Kind = a.Kind.ToString().Split('(').[0] })

    /// Chip label for a completed step.
    let private chipForStep (step: PaletteStep) (values: Map<string, string>) : PaletteChip list =
        match step with
        | RefStep(key, label, _) ->
            let v = values |> Map.tryFind key |> Option.defaultValue "\u2013"
            [ { Label = label; Value = v } ]
        | ScalarsStep(_, fields) ->
            fields |> List.map (fun f ->
                let v = values |> Map.tryFind f.Key |> Option.defaultValue (string f.Default)
                { Label = f.Label; Value = v })

    // ── Public API ────────────────────────────────────────────────────

    let empty : PaletteSession =
        { PickedKind = None; Steps = []; StepIndex = -1; Values = Map.empty; Query = "" }

    let toState (session: PaletteSession) (typeMap: Map<ActionId, FieldType>) (doc: Document) : PaletteState =
        let closed =
            { IsOpen = false; Mode = "closed"; PickedKind = None; Chips = []
              Prompt = ""; Items = []; ScalarFields = []; HintBar = [] }
        if session.StepIndex < 0 then closed
        else
        match session.PickedKind with
        | None ->
            { IsOpen = true; Mode = "command"; PickedKind = None; Chips = []
              Prompt = "Add action\u2026"
              Items = filterTemplates session.Query
              ScalarFields = []
              HintBar = [ "\u2191\u2193 navigate"; "\u21B5 select"; "esc cancel" ] }
        | Some kind ->
            let steps = session.Steps
            if session.StepIndex >= steps.Length then
                { closed with Mode = "done"; PickedKind = Some kind }
            else
                let chips =
                    steps.[.. session.StepIndex - 1]
                    |> List.collect (fun st -> chipForStep st session.Values)
                let step = steps.[session.StepIndex]
                match step with
                | RefStep(_, label, accepts) ->
                    { IsOpen = true; Mode = "ref"; PickedKind = Some kind; Chips = chips
                      Prompt = $"Pick \"{label}\" for {kind}\u2026"
                      Items = filterActions session.Query accepts typeMap doc
                      ScalarFields = []
                      HintBar = [ "\u2191\u2193 navigate"; "\u21B5 next"; "\u2318\u21B5 create now"; "\u232B back"; "esc cancel" ] }
                | ScalarsStep(_, fields) ->
                    let scalarFields =
                        fields |> List.map (fun f ->
                            let v =
                                session.Values
                                |> Map.tryFind f.Key
                                |> Option.bind (fun s -> match System.Double.TryParse(s) with true, x -> Some x | _ -> None)
                                |> Option.defaultValue f.Default
                            { Key = f.Key; Label = f.Label; Value = v })
                    { IsOpen = true; Mode = "scalars"; PickedKind = Some kind; Chips = chips
                      Prompt = ""
                      Items = []
                      ScalarFields = scalarFields
                      HintBar = [ "drag to adjust"; "\u21B5 next"; "\u2318\u21B5 create now"; "\u232B back"; "esc cancel" ] }

    let openSession () : PaletteSession =
        { empty with StepIndex = 0; Query = "" }

    let setQuery (query: string) (session: PaletteSession) : PaletteSession =
        { session with Query = query }

    let pickCommand (kindCase: string) (session: PaletteSession) : PaletteSession =
        let steps = stepsFor kindCase
        let seeded =
            steps
            |> List.collect (fun step ->
                match step with
                | ScalarsStep(_, fields) -> fields |> List.map (fun f -> f.Key, string f.Default)
                | _ -> [])
            |> Map.ofList
        { session with PickedKind = Some kindCase; Steps = steps; StepIndex = 0; Values = seeded; Query = "" }

    let pickItem (itemId: string) (session: PaletteSession) : PaletteSession =
        if session.StepIndex >= session.Steps.Length then session
        else
            match session.Steps.[session.StepIndex] with
            | RefStep(key, _, _) ->
                let values = session.Values |> Map.add key itemId
                { session with Values = values; StepIndex = session.StepIndex + 1; Query = "" }
            | _ -> session

    /// Commit the current scalars step and advance.
    let commitScalars (session: PaletteSession) : PaletteSession =
        { session with StepIndex = session.StepIndex + 1; Query = "" }

    /// Update a single scalar field value (fire-and-forget during drag).
    let setScalarField (key: string) (value: float) (session: PaletteSession) : PaletteSession =
        { session with Values = session.Values |> Map.add key (string value) }

    let back (session: PaletteSession) : PaletteSession =
        if session.StepIndex > 0 then
            { session with StepIndex = session.StepIndex - 1; Query = "" }
        else
            { PickedKind = None; Steps = []; StepIndex = 0; Values = Map.empty; Query = "" }

    let skipToEnd (session: PaletteSession) : PaletteSession =
        { session with StepIndex = session.Steps.Length }

    /// Build a DocAction from the completed palette session.
    let buildAction (session: PaletteSession) (idSuffix: string) : DocAction option =
        match session.PickedKind with
        | None -> None
        | Some kind ->
            let v = session.Values
            let str key = v |> Map.tryFind key
            let flt key def =
                v |> Map.tryFind key
                |> Option.bind (fun s -> match System.Double.TryParse(s) with true, f -> Some f | _ -> None)
                |> Option.defaultValue def
            let int key def =
                v |> Map.tryFind key
                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, i -> Some i | _ -> None)
                |> Option.defaultValue def

            let actionKind =
                match kind with
                | "Sphere" -> Sphere(flt "radius" 8.0)
                | "Cylinder" -> Cylinder(flt "radius" 5.0, flt "height" 20.0)
                | "Box" -> Box(flt "width" 10.0, flt "height" 10.0, flt "depth" 10.0)
                | "HalfPlane" -> HalfPlane("Z", flt "offset" 0.0, false)
                | "Translate" -> Translate(str "child", flt "x" 0.0, flt "y" 0.0, flt "z" 0.0)
                | "Rotate" -> Rotate(str "child", flt "ax" 0.0, flt "ay" 0.0, flt "az" 1.0, flt "angle" 0.0)
                | "Move" -> ActionKind.Move(str "child", str "frame")
                | "Union" -> Union(str "a", str "b", flt "radius" 0.0)
                | "Subtract" -> Subtract(str "a", str "b", flt "radius" 0.0)
                | "Intersect" -> Intersect(str "a", str "b", flt "radius" 0.0)
                | "Sketch" -> ActionKind.Sketch(str "origin", SketchPlane.defaults, ActionSketch.empty)
                | "FromSketch" -> FromSketch(str "child", false, FromSketchSelection.defaults)
                | "Thicken" -> Thicken(str "child", flt "amount" 2.0)
                | "Shell" -> Shell(str "child", flt "thickness" 1.0)
                | "Mesh" -> Mesh(str "child", flt "size" 0.2, int "resolution" 96)
                | _ -> Origin

            let id = kind.ToLowerInvariant() + "_" + idSuffix
            Some { Id = id; Name = None; Kind = actionKind; Visible = true; Display = None; FieldSlice = None }
