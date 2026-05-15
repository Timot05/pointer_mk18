module PointerMk18.Ui.Icons

open Fable.Core
open Fable.Core.JsInterop
open Server.Lang
open Browser.Dom
open Browser.Types

// Carbon icon rendering. Each @carbon/icons entry exports a descriptor:
// { elem: string, attrs: {...}, content?: Desc[] }.

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
let private layers          : obj = importDefault "@carbon/icons/es/layers/16"
let private view            : obj = importDefault "@carbon/icons/es/view/16"
let private logoGithub      : obj = importDefault "@carbon/icons/es/logo--github/16"
let private rotateClockwise : obj = importDefault "@carbon/icons/es/rotate--clockwise/16"

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

let private descriptorForSpecName (name: string) : obj =
    match name with
    | "sphere" -> circleOutline
    | "cylinder" -> db2Database
    | "box" -> cube
    | "translate" -> moveIcon
    | "mirror-symmetric" -> rotateIcon
    | "union" -> joinFull
    | "intersect" -> joinInner
    | "subtract" -> joinLeft
    | "thicken" -> containerImage
    | "shell" -> circleDash
    | "from-sketch" -> shapeUnite
    | "revolve" -> rotateClockwise
    | "wing-remap-preview" -> zAxis
    | _ -> layers

let forSpecName (name: string) : Element =
    buildSvg (descriptorForSpecName name)

let forBody (body: Notebook.BlockBody) : Element =
    match body with
    | Notebook.SketchBody _ -> buildSvg pen
    | Notebook.NativeBody(name, _) -> forSpecName name

let fallback () : Element =
    buildSvg layers

let eye () : Element =
    buildSvg view

let github () : Element =
    buildSvg logoGithub
