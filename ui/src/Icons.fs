module PointerMk18.Ui.Icons

open Fable.Core
open Fable.Core.JsInterop
open Server
open Browser.Dom
open Browser.Types

// ---------------------------------------------------------------------------
// Carbon icon rendering. Ported from user-interface/src/icons.ts.
//
// Each @carbon/icons entry exports a descriptor shaped as
//   { elem: string, attrs: {...}, content?: Desc[] }
// We consume them as untyped `obj` via Fable's `importDefault` and walk the
// tree to build real SVG DOM nodes.
// ---------------------------------------------------------------------------

let private svgNs = "http://www.w3.org/2000/svg"

let private zAxis           : obj = importDefault "@carbon/icons/es/z-axis/16"
let private circleOutline   : obj = importDefault "@carbon/icons/es/circle--outline/16"
let private db2Database     : obj = importDefault "@carbon/icons/es/db2--database/16"
let private cube            : obj = importDefault "@carbon/icons/es/cube/16"
let private squareOutline   : obj = importDefault "@carbon/icons/es/square--outline/16"
let private pen             : obj = importDefault "@carbon/icons/es/pen/16"
let private shapeUnite      : obj = importDefault "@carbon/icons/es/shape--unite/16"
let private moveIcon        : obj = importDefault "@carbon/icons/es/move/16"
let private rotateIcon      : obj = importDefault "@carbon/icons/es/rotate/16"
let private joinFull        : obj = importDefault "@carbon/icons/es/join--full/16"
let private joinInner       : obj = importDefault "@carbon/icons/es/join--inner/16"
let private joinLeft        : obj = importDefault "@carbon/icons/es/join--left/16"
let private containerImage  : obj = importDefault "@carbon/icons/es/container-image--pull/16"
let private circleDash      : obj = importDefault "@carbon/icons/es/circle-dash/16"
let private triangleOutline : obj = importDefault "@carbon/icons/es/triangle--outline/16"
let private layers          : obj = importDefault "@carbon/icons/es/layers/16"

// --- Descriptor walkers --------------------------------------------------

[<Emit("Object.entries($0)")>]
let private entries (o: obj) : (string * obj)[] = jsNative

let rec private buildSvgNode (desc: obj) : Element =
    let node = document.createElementNS (svgNs, unbox<string> desc?elem)
    let attrs = desc?attrs
    if not (isNull attrs) then
        for (k, v) in entries attrs do
            node.setAttribute (k, string v)
    let content = desc?content
    if not (isNull content) then
        for child in unbox<obj[]> content do
            node.appendChild (buildSvgNode child :> Node) |> ignore
    node

let private buildSvg (desc: obj) : Element =
    let svg = document.createElementNS (svgNs, "svg")
    for (k, v) in entries desc?attrs do
        svg.setAttribute (k, string v)
    for child in unbox<obj[]> desc?content do
        svg.appendChild (buildSvgNode child :> Node) |> ignore
    svg

// --- Public API: map F# domain values to icons ---------------------------

let private descriptorFor (kind: ActionKind) : obj =
    match kind with
    | Origin -> zAxis
    | Cylinder _ -> db2Database
    | Sphere _ -> circleOutline
    | Box _ -> cube
    | HalfPlane _ -> squareOutline
    | Translate _ -> moveIcon
    | Rotate _ -> rotateIcon
    | Move _ -> moveIcon
    | Union _ -> joinFull
    | Intersect _ -> joinInner
    | Subtract _ -> joinLeft
    | Sketch _ -> pen
    | FromSketch _ -> shapeUnite
    | Thicken _ -> containerImage
    | Shell _ -> circleDash
    | Mesh _ -> triangleOutline

let private descriptorForTemplate (t: ActionTemplate) : obj =
    match t with
    | SphereTemplate -> circleOutline
    | CylinderTemplate -> db2Database
    | BoxTemplate -> cube
    | HalfPlaneTemplate -> squareOutline
    | TranslateTemplate -> moveIcon
    | RotateTemplate -> rotateIcon
    | MoveTemplate -> moveIcon
    | UnionTemplate -> joinFull
    | SubtractTemplate -> joinLeft
    | IntersectTemplate -> joinInner
    | SketchTemplate -> pen
    | FromSketchTemplate -> shapeUnite
    | ThickenTemplate -> containerImage
    | ShellTemplate -> circleDash
    | MeshTemplate -> triangleOutline

//Render a Carbon SVG icon for an ActionKind. The returned node can be
//appended directly to an HTMLElement.
let forKind (kind: ActionKind) : Element =
    buildSvg (descriptorFor kind)

//Render a Carbon SVG icon for an ActionTemplate (used in the "+ Add" menu
//before a concrete action has been created).
let forTemplate (t: ActionTemplate) : Element =
    buildSvg (descriptorForTemplate t)

//Fallback icon when no specific kind/template is known.
let fallback () : Element =
    buildSvg layers
