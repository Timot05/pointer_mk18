import { Record } from "../ui/fable_modules/fable-library-js.4.29.0/Types.js";
import { class_type, record_type, float64_type, string_type, int32_type } from "../ui/fable_modules/fable-library-js.4.29.0/Reflection.js";
import { PromiseBuilder__Delay_62FBFDE1, PromiseBuilder__Run_212F1D4B } from "../ui/fable_modules/Fable.Promise.3.2.0/Promise.fs.js";
import { promise } from "../ui/fable_modules/Fable.Promise.3.2.0/PromiseImpl.fs.js";
import { ofArray } from "../ui/fable_modules/fable-library-js.4.29.0/Map.js";
import { map } from "../ui/fable_modules/fable-library-js.4.29.0/Array.js";
import { comparePrimitives } from "../ui/fable_modules/fable-library-js.4.29.0/Util.js";
import { printf, toText } from "../ui/fable_modules/fable-library-js.4.29.0/String.js";
import { GPUTextureDescriptor, GPUTextureUsage_RenderAttachment, GPUTextureUsage_CopyDst, GPUTextureUsage_TextureBinding, GPUExtent3D } from "./WebGPU.fs.js";

export class FontChar extends Record {
    constructor(Id, Char, Width, Height, XOffset, YOffset, XAdvance, X, Y) {
        super();
        this.Id = (Id | 0);
        this.Char = Char;
        this.Width = Width;
        this.Height = Height;
        this.XOffset = XOffset;
        this.YOffset = YOffset;
        this.XAdvance = XAdvance;
        this.X = X;
        this.Y = Y;
    }
}

export function FontChar_$reflection() {
    return record_type("MsdfAtlas.FontChar", [], FontChar, () => [["Id", int32_type], ["Char", string_type], ["Width", float64_type], ["Height", float64_type], ["XOffset", float64_type], ["YOffset", float64_type], ["XAdvance", float64_type], ["X", float64_type], ["Y", float64_type]]);
}

export class FontMetrics extends Record {
    constructor(Chars, Kernings, LineHeight, Base, ScaleW, ScaleH, DistanceRange) {
        super();
        this.Chars = Chars;
        this.Kernings = Kernings;
        this.LineHeight = LineHeight;
        this.Base = Base;
        this.ScaleW = ScaleW;
        this.ScaleH = ScaleH;
        this.DistanceRange = DistanceRange;
    }
}

export function FontMetrics_$reflection() {
    return record_type("MsdfAtlas.FontMetrics", [], FontMetrics, () => [["Chars", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, FontChar_$reflection()])], ["Kernings", class_type("Microsoft.FSharp.Collections.FSharpMap`2", [string_type, float64_type])], ["LineHeight", float64_type], ["Base", float64_type], ["ScaleW", float64_type], ["ScaleH", float64_type], ["DistanceRange", float64_type]]);
}

export class MsdfAtlas extends Record {
    constructor(Texture, Sampler, Width, Height) {
        super();
        this.Texture = Texture;
        this.Sampler = Sampler;
        this.Width = (Width | 0);
        this.Height = (Height | 0);
    }
}

export function MsdfAtlas_$reflection() {
    return record_type("MsdfAtlas.MsdfAtlas", [], MsdfAtlas, () => [["Texture", class_type("WebGPU.IGPUTexture")], ["Sampler", class_type("WebGPU.IGPUSampler")], ["Width", int32_type], ["Height", int32_type]]);
}

function charsField(json) {
    return json.chars;
}

function kerningsField(json) {
    if (json.kernings == null) {
        return [];
    }
    else {
        return json.kernings;
    }
}

export function loadMetrics(url) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((fetch(url).then(r => r.json())).then((_arg) => {
        const json = _arg;
        const chars = ofArray(map((c) => {
            const fc = new FontChar(c.id, c.char, c.width, c.height, c.xoffset, c.yoffset, c.xadvance, c.x, c.y);
            return [fc.Char, fc];
        }, charsField(json)), {
            Compare: comparePrimitives,
        });
        const kernings = ofArray(map((k) => {
            const first = k.first | 0;
            const second = k.second | 0;
            const amount = k.amount;
            return [toText(printf("%d:%d"))(first)(second), amount];
        }, kerningsField(json)), {
            Compare: comparePrimitives,
        });
        let distanceRange;
        const df = json.distanceField;
        if (df == null) {
            distanceRange = 4;
        }
        else {
            const dr = df.distanceRange;
            distanceRange = ((dr == null) ? 4 : dr);
        }
        return Promise.resolve(new FontMetrics(chars, kernings, json.common.lineHeight, json.common.base, json.common.scaleW, json.common.scaleH, distanceRange));
    }))));
}

export function loadAtlas(device, url) {
    return PromiseBuilder__Run_212F1D4B(promise, PromiseBuilder__Delay_62FBFDE1(promise, () => ((fetch(url).then(r => r.blob()).then(b => createImageBitmap(b, { colorSpaceConversion: 'none' }))).then((_arg) => {
        const image = _arg;
        const w = (image.width) | 0;
        const h = (image.height) | 0;
        const texture = device.createTexture(new GPUTextureDescriptor(new GPUExtent3D(w, h, 1), "rgba8unorm", (GPUTextureUsage_TextureBinding | GPUTextureUsage_CopyDst) | GPUTextureUsage_RenderAttachment));
        device.queue.copyExternalImageToTexture({ source: image }, { texture: texture }, [w, h]);
        const sampler = device.createSampler({
            addressModeU: "clamp-to-edge",
            addressModeV: "clamp-to-edge",
            magFilter: "linear",
            minFilter: "linear",
        });
        return Promise.resolve(new MsdfAtlas(texture, sampler, w, h));
    }))));
}

