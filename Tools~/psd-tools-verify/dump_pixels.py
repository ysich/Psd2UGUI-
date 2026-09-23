#!/usr/bin/env python3
"""导出 psd-tools 视角的图层像素与元信息，供 reference 侧使用。

用法：
    python dump_pixels.py <file.psd> <outdir>

输出（与 PsdDump 的 `--pixels` / `--composite` 一一对应）：
    <outdir>/<layer_id>.rgba    每像素 4 字节，行优先，直通 alpha（不预乘）
    <outdir>/composite.rgba     psd-tools 自己合成的整图（会烘焙图层效果，仅供参照）
    <outdir>/index.json         {"document": {width,height}, "layers": [...]}
                                layers 按图层记录顺序排列，带 x/y/opacity/visible

注意：`layer.numpy()` 不含图层蒙版、裁剪关系与图层效果，正是我们想要的“图层自身像素”。
"""

import json
import os
import sys

import numpy as np
from psd_tools import PSDImage


def to_uint8(array):
    if array.dtype == np.uint8:
        return array.copy()
    # psd-tools 的 numpy() 给的是 0~1 的浮点
    return np.clip(np.rint(array.astype(np.float64) * 255.0), 0, 255).astype(np.uint8)


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)

    psd_path, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    psd = PSDImage.open(psd_path)

    layers = []
    for layer in psd.descendants():
        if layer.is_group():
            continue

        try:
            array = layer.numpy()
        except Exception as error:  # 部分图层没有可解码的通道
            print("跳过 %s: %s" % (layer.name, error))
            continue

        if array is None or array.size == 0:
            continue

        if array.ndim == 2:
            array = np.dstack([array, array, array, np.ones_like(array)])

        data = to_uint8(array)
        with open(os.path.join(outdir, "%d.rgba" % layer.layer_id), "wb") as handle:
            handle.write(data.tobytes())
        layers.append({
            "id": int(layer.layer_id),
            "name": layer.name,
            "x": int(layer.offset[0]),
            "y": int(layer.offset[1]),
            "width": int(data.shape[1]),
            "height": int(data.shape[0]),
            "opacity": int(layer.opacity),
            "visible": bool(layer.visible),
        })

    index = {"document": {"width": int(psd.width), "height": int(psd.height)}, "layers": layers}
    with open(os.path.join(outdir, "index.json"), "w") as handle:
        json.dump(index, handle, indent=2, ensure_ascii=False)

    composite = psd.composite(ignore_preview=True)
    if composite is not None:
        with open(os.path.join(outdir, "composite.rgba"), "wb") as handle:
            handle.write(np.asarray(composite.convert("RGBA"), dtype=np.uint8).tobytes())

    print("图层像素 %d 个，画布 %dx%d" % (len(layers), psd.width, psd.height))
    return 0


if __name__ == "__main__":
    sys.exit(main())
