export interface MsdfAtlas {
  texture: GPUTexture;
  sampler: GPUSampler;
  width: number;
  height: number;
}

export interface FontChar {
  id: number;
  char: string;
  width: number;
  height: number;
  xoffset: number;
  yoffset: number;
  xadvance: number;
  x: number;
  y: number;
}

interface FontKernPair {
  first: number;
  second: number;
  amount: number;
}

interface FontJson {
  chars: FontChar[];
  common: {
    lineHeight: number;
    base: number;
    scaleW: number;
    scaleH: number;
  };
  distanceField?: {
    distanceRange?: number;
  };
  kernings?: FontKernPair[];
}

export interface FontMetrics {
  chars: Map<string, FontChar>;
  kernings: Map<string, number>;
  lineHeight: number;
  base: number;
  scaleW: number;
  scaleH: number;
  distanceRange: number;
}

export async function loadMsdfAtlas(device: GPUDevice, atlasUrl: string): Promise<MsdfAtlas> {
  const response = await fetch(atlasUrl);
  if (!response.ok) {
    throw new Error(`MSDF atlas fetch failed: ${response.status} ${response.statusText}`);
  }
  const blob = await response.blob();
  const image = await createImageBitmap(blob, { colorSpaceConversion: "none" });

  const texture = device.createTexture({
    label: "msdf_atlas",
    size: [image.width, image.height],
    format: "rgba8unorm",
    usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.RENDER_ATTACHMENT,
  });
  device.queue.copyExternalImageToTexture({ source: image }, { texture }, [image.width, image.height]);

  const sampler = device.createSampler({
    label: "msdf_atlas_sampler",
    magFilter: "linear",
    minFilter: "linear",
    addressModeU: "clamp-to-edge",
    addressModeV: "clamp-to-edge",
  });

  return { texture, sampler, width: image.width, height: image.height };
}

export async function loadFontMetrics(url: string): Promise<FontMetrics> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Font metrics fetch failed: ${response.status} ${response.statusText}`);
  }
  const json = await response.json() as FontJson;
  const chars = new Map(json.chars.map((char) => [char.char, char]));
  const kernings = new Map<string, number>();
  for (const pair of json.kernings ?? []) {
    kernings.set(`${pair.first}:${pair.second}`, pair.amount);
  }
  return {
    chars,
    kernings,
    lineHeight: json.common.lineHeight,
    base: json.common.base,
    scaleW: json.common.scaleW,
    scaleH: json.common.scaleH,
    distanceRange: json.distanceField?.distanceRange ?? 4,
  };
}
