namespace Server

// ---------------------------------------------------------------------------
// Element tree — intermediate representation between actions and field IR.
//
// Every Element node carries the ActionId it originated from, so FieldCompile
// can allocate slot refs for each numeric value. Transforms are NOT baked:
// ETranslate/ERotate keep their parameters, and Move consumers wrap their
// child with the frame's transform chain (slots shared with the frame's own
// actions).
// ---------------------------------------------------------------------------

/// A single link in a frame chain — Frame-typed actions are represented as
/// a list of these, rooted at Origin (the empty chain is identity).
type FrameStep =
    | FrameTranslate of actionId: ActionId * x: float * y: float * z: float
    | FrameRotate of actionId: ActionId * ax: float * ay: float * az: float * angle: float

type FrameChain = FrameStep list   // child/local step first, outer result built by left-fold

/// Scene element — a recursive tree of geometry operations for Field-typed
/// actions. Each node carries its source ActionId for slot allocation.
type Element =
    | EEmpty
    | ESphere of actionId: ActionId * radius: float
    | ECylinder of actionId: ActionId * radius: float * height: float
    | EBox of actionId: ActionId * width: float * height: float * depth: float
    | EHalfPlane of actionId: ActionId * axis: string * offset: float * flip: bool
    | ETranslate of actionId: ActionId * x: float * y: float * z: float * child: Element
    | ERotate of actionId: ActionId * ax: float * ay: float * az: float * angle: float * child: Element
    | EUnion of actionId: ActionId * a: Element * b: Element * radius: float
    | ESubtract of actionId: ActionId * a: Element * b: Element * radius: float
    | EIntersect of actionId: ActionId * a: Element * b: Element * radius: float
    | EThicken of actionId: ActionId * child: Element * amount: float
    | EShell of actionId: ActionId * child: Element * thickness: float
    /// FromSketch — compiles to FSketch at the field IR layer.
    /// Carries a snapshot of the source sketch's entities/constraints so
    /// the field compile doesn't need to look actions back up.
    | EFromSketch of
        actionId: ActionId *          // the FromSketch action
        sketchActionId: ActionId *    // the source Sketch action
        sketch: ActionSketch *
        selection: FromSketchSelection *
        flip: bool

type BuildResult =
    { Elements: Map<ActionId, Element>
      Frames: Map<ActionId, FrameChain> }

module Element =

    /// Wrap a field child with a frame's transform chain.
    /// Steps are ordered from child/local to parent/world.
    let rec applyFrame (chain: FrameChain) (child: Element) : Element =
        match chain with
        | [] -> child
        | FrameTranslate(id, x, y, z) :: rest ->
            ETranslate(id, x, y, z, applyFrame rest child)
        | FrameRotate(id, ax, ay, az, angle) :: rest ->
            ERotate(id, ax, ay, az, angle, applyFrame rest child)

    /// Build element trees and frame chains from a type-checked action graph.
    /// Only Field-typed actions get Element entries; Frame-typed actions get
    /// FrameChain entries instead.
    let build (actions: DocAction list) (typeMap: Map<ActionId, FieldType>) : BuildResult =
        let actionMap = actions |> List.map (fun a -> a.Id, a) |> Map.ofList
        let isField id =
            match Map.tryFind id typeMap with
            | Some FieldType.Field -> true
            | _ -> false

        // Frame chain resolution — for Frame-typed actions only.
        // Child/local step first, so Origin -> Translate -> Rotate becomes
        // [Translate; Rotate], which composes to T * R.
        let rec frameChain (id: ActionId) (cache: Map<ActionId, FrameChain>) : FrameChain * Map<ActionId, FrameChain> =
            match Map.tryFind id cache with
            | Some c -> c, cache
            | None ->
                match Map.tryFind id actionMap with
                | None -> [], cache
                | Some action ->
                    let chain, cache =
                        match action.Kind with
                        | Origin -> [], cache
                        | Translate(child, x, y, z) ->
                            let base', cache =
                                match child with
                                | Some cid -> frameChain cid cache
                                | None -> [], cache
                            base' @ [ FrameTranslate(action.Id, x, y, z) ], cache
                        | Rotate(child, ax, ay, az, angle) ->
                            let base', cache =
                                match child with
                                | Some cid -> frameChain cid cache
                                | None -> [], cache
                            base' @ [ FrameRotate(action.Id, ax, ay, az, angle) ], cache
                        | _ -> [], cache
                    chain, Map.add id chain cache

        // Element compilation for Field-typed actions
        let rec compile (id: ActionId) (state: Map<ActionId, Element> * Map<ActionId, FrameChain>) : Element * (Map<ActionId, Element> * Map<ActionId, FrameChain>) =
            let (built, frames) = state
            match Map.tryFind id built with
            | Some elem -> elem, state
            | None ->
                match Map.tryFind id actionMap with
                | None -> EEmpty, state
                | Some action ->
                    let elem, state = compileKind action state
                    let built', frames' = state
                    elem, (Map.add id elem built', frames')

        and resolveChild (id: ActionId option) (state: Map<ActionId, Element> * Map<ActionId, FrameChain>) : Element * (Map<ActionId, Element> * Map<ActionId, FrameChain>) =
            match id with
            | None -> EEmpty, state
            | Some childId -> compile childId state

        and resolveFrame (id: ActionId option) (state: Map<ActionId, Element> * Map<ActionId, FrameChain>) : FrameChain * (Map<ActionId, Element> * Map<ActionId, FrameChain>) =
            let (built, frames) = state
            match id with
            | None -> [], state
            | Some fid ->
                let chain, frames' = frameChain fid frames
                chain, (built, frames')

        and compileKind (action: DocAction) (state: Map<ActionId, Element> * Map<ActionId, FrameChain>) : Element * (Map<ActionId, Element> * Map<ActionId, FrameChain>) =
            let id = action.Id
            match action.Kind with
            | Origin ->
                // Origin produces a frame, not a field element. Its frame chain
                // is [] (identity). Not a field, so EEmpty.
                EEmpty, state

            | Sphere r -> ESphere(id, r), state
            | Cylinder(r, h) -> ECylinder(id, r, h), state
            | Box(w, h, d) -> EBox(id, w, h, d), state
            | HalfPlane(ax, off, fl) -> EHalfPlane(id, ax, off, fl), state
            | Sketch _ -> EEmpty, state

            | Translate(child, x, y, z) ->
                // If this Translate is part of a frame chain, its field output
                // is empty. But since we're called for a Field-typed action,
                // typecheck has already determined the child is a Field.
                // Wrap the child with this translate.
                let childElem, state = resolveChild child state
                ETranslate(id, x, y, z, childElem), state

            | Rotate(child, ax, ay, az, angle) ->
                let childElem, state = resolveChild child state
                ERotate(id, ax, ay, az, angle, childElem), state

            | Move(child, frame) ->
                // Move applies a frame's transform chain to a field child.
                let childElem, state = resolveChild child state
                let chain, state = resolveFrame frame state
                applyFrame chain childElem, state

            | Union(a, b, r) ->
                let ea, state = resolveChild a state
                let eb, state = resolveChild b state
                EUnion(id, ea, eb, r), state

            | Subtract(a, b, r) ->
                let ea, state = resolveChild a state
                let eb, state = resolveChild b state
                ESubtract(id, ea, eb, r), state

            | Intersect(a, b, r) ->
                let ea, state = resolveChild a state
                let eb, state = resolveChild b state
                EIntersect(id, ea, eb, r), state

            | Thicken(child, amt) ->
                let childElem, state = resolveChild child state
                EThicken(id, childElem, amt), state

            | Shell(child, t) ->
                let childElem, state = resolveChild child state
                EShell(id, childElem, t), state

            | FromSketch(child, flip, selection) ->
                // Look up the source Sketch action to snapshot its entities
                // and its origin frame chain.
                match child with
                | None -> EEmpty, state
                | Some sketchId ->
                    match Map.tryFind sketchId actionMap with
                    | Some sketchAction ->
                        match sketchAction.Kind with
                        | Sketch(originId, plane, sketch) ->
                            let frameChain', state =
                                match originId with
                                | Some fid ->
                                    let (built, frames) = state
                                    let c, frames' = frameChain fid frames
                                    c, (built, frames')
                                | None -> [], state
                            let core =
                                EFromSketch(id, sketchId, sketch, selection, flip)
                            let withPlane =
                                match plane with
                                | XY -> core
                                | XZ -> ERotate($"{id}_plane", 1.0, 0.0, 0.0, System.Math.PI * 0.5, core)
                                | YZ -> ERotate($"{id}_plane_z", 0.0, 0.0, 1.0, System.Math.PI * 0.5, ERotate($"{id}_plane_x", 1.0, 0.0, 0.0, System.Math.PI * 0.5, core))
                            applyFrame frameChain' withPlane, state
                        | _ -> EEmpty, state
                    | None -> EEmpty, state

            | Mesh(child, _, _) ->
                // Mesh is a type-converter to Mesh, not Field.
                let _childElem, state = resolveChild child state
                EEmpty, state

        let mutable state : Map<ActionId, Element> * Map<ActionId, FrameChain> = Map.empty, Map.empty
        for action in actions do
            // Eagerly build Field-typed visible actions as Elements.
            if action.Visible && isField action.Id then
                let _, s = compile action.Id state
                state <- s
            // Eagerly resolve Frame-typed actions as frame chains (so Move
            // lookups always find them, even if the frame action itself
            // isn't "visible").
            match Map.tryFind action.Id typeMap with
            | Some FieldType.Frame ->
                let (built, frames) = state
                let _, frames' = frameChain action.Id frames
                state <- (built, frames')
            | _ -> ()
        let (elements, frames) = state
        { Elements = elements; Frames = frames }
