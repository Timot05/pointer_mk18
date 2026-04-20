module ViewerColors

// Shared colour constants for the viewer. Direct F# port of colors.ts.
// RGBA components are float [0,1] matching WebGPU colour inputs.

let PAGE_BG = "#FEFCF3"
let SURFACE_BG = "#F5F0E4"
let SURFACE_BORDER = "#D5D0C4"
let TEXT_PRIMARY = "#333333"
let TEXT_MUTED = "#666666"

let ACCENT : float[]       = [| 0.502; 0.745; 0.549; 1.0 |]
let ACCENT_SOFT : float[]  = [| 0.502; 0.745; 0.549; 0.2 |]
let LOOP_FILL : float[]    = [| 0.741; 0.694; 0.575; 0.18 |]
let GRID_MINOR : float[]   = [| 0.835; 0.816; 0.769; 0.35 |]
let GRID_MAJOR : float[]   = [| 0.835; 0.816; 0.769; 0.75 |]
let AXIS : float[]         = [| 0.6;   0.58;  0.52;  0.9 |]
let SKETCH_LINE : float[]  = [| 0.231; 0.231; 0.231; 1.0 |]
let SKETCH_POINT : float[] = [| 0.231; 0.231; 0.231; 1.0 |]
let DIM_COLOR : float[]    = [| 0.427; 0.341; 0.192; 1.0 |]
let DIM_HOVER : float[]    = [| 0.725; 0.510; 0.170; 1.0 |]
let FIXED_COLOR : float[]  = [| 0.690; 0.350; 0.416; 1.0 |]
