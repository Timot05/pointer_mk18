module MsdfAtlas

// MSDF bitmap-font atlas loader + metrics parser.
// Direct port of ui/viewer/msdf-atlas.ts.

open Fable.Core
open Fable.Core.JsInterop
open WebGPU

type FontChar =
    { Id: int
      Char: string
      Width: float
      Height: float
      XOffset: float
      YOffset: float
      XAdvance: float
      X: float
      Y: float }

type FontMetrics =
    { Chars: Map<string, FontChar>
      Kernings: Map<string, float>
      LineHeight: float
      Base: float
      ScaleW: float
      ScaleH: float
      DistanceRange: float }

type MsdfAtlas =
    { Texture: IGPUTexture
      Sampler: IGPUSampler
      Width: int
      Height: int }

let private charsField (json: obj) : obj[] = unbox (json?chars)
let private kerningsField (json: obj) : obj[] =
    if isNull (json?kernings) then [||] else unbox (json?kernings)

[<Emit("$0.common.base")>]
let private commonBase (json: obj) : float = jsNative

let loadMetrics (url: string) : JS.Promise<FontMetrics> = promise {
    let! json = fetchJson url
    let chars =
        charsField json
        |> Array.map (fun c ->
            let fc =
                { Id = unbox c?id
                  Char = unbox c?char
                  Width = unbox c?width
                  Height = unbox c?height
                  XOffset = unbox c?xoffset
                  YOffset = unbox c?yoffset
                  XAdvance = unbox c?xadvance
                  X = unbox c?x
                  Y = unbox c?y }
            fc.Char, fc)
        |> Map.ofArray
    let kernings =
        kerningsField json
        |> Array.map (fun k ->
            let first : int = unbox k?first
            let second : int = unbox k?second
            let amount : float = unbox k?amount
            sprintf "%d:%d" first second, amount)
        |> Map.ofArray
    let distanceRange : float =
        let df = json?distanceField
        if isNull df then 4.0
        else
            let dr = df?distanceRange
            if isNull dr then 4.0 else unbox dr
    return
        { Chars = chars
          Kernings = kernings
          LineHeight = unbox json?common?lineHeight
          Base = commonBase json
          ScaleW = unbox json?common?scaleW
          ScaleH = unbox json?common?scaleH
          DistanceRange = distanceRange }
}

let loadAtlas (device: IGPUDevice) (url: string) : JS.Promise<MsdfAtlas> = promise {
    let! image = fetchImageBitmap url
    let w = imageWidth image
    let h = imageHeight image
    let texture =
        device.createTexture
            { size = { width = w; height = h; depthOrArrayLayers = 1 }
              format = "rgba8unorm"
              usage =
                GPUTextureUsage.TextureBinding
                ||| GPUTextureUsage.CopyDst
                ||| GPUTextureUsage.RenderAttachment }
    copyImageBitmapToTexture device image texture w h
    let sampler =
        device.createSampler
            (box
                {| magFilter = "linear"
                   minFilter = "linear"
                   addressModeU = "clamp-to-edge"
                   addressModeV = "clamp-to-edge" |})
    return
        { Texture = texture
          Sampler = sampler
          Width = w
          Height = h }
}
