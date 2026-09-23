#!/usr/bin/env python3
"""用独立解码器（Pillow）检查导出的 Sprite PNG 与九宫结果。

用法：
    python check_sprites.py <sprites_dir> <pixel_reference_dir>

- <sprites_dir> 由 `PsdDump --sprites` 生成
- <pixel_reference_dir> 由 dump_pixels.py 生成（psd-tools 的图层像素）

检查内容：
  1. PNG 能被 Pillow 正常解码，尺寸与 index.json 一致，颜色模式是 RGBA
  2. 用「Sprite + Border」按原尺寸还原，必须与 psd-tools 的图层像素（去空后）逐像素一致
     —— 这一步同时验证了 PNG 编码、九宫最小化与边框语义

退出码 0 表示全部通过。
"""

import json
import os
import sys

import numpy as np
from PIL import Image


def expand(sprite, border, width, height):
    """按 uGUI 九宫语义把 sprite 还原成指定尺寸（最近邻）。"""
    left, bottom, right, top = border
    sprite_height, sprite_width = sprite.shape[:2]

    xs = []
    for x in range(width):
        if x < left:
            xs.append(x)
        elif x >= width - right:
            xs.append(sprite_width - (width - x))
        else:
            xs.append(left)

    ys = []
    for y in range(height):
        if y < top:
            ys.append(y)
        elif y >= height - bottom:
            ys.append(sprite_height - (height - y))
        else:
            ys.append(top)

    return sprite[np.ix_(ys, xs)]


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)

    sprites_dir, reference_dir = sys.argv[1], sys.argv[2]
    entries = json.load(open(os.path.join(sprites_dir, "index.json")))

    sliced = 0
    problems = []
    for entry in entries:
        image = Image.open(os.path.join(sprites_dir, entry["file"]))
        if image.mode != "RGBA":
            problems.append("[%s] %s 颜色模式是 %s，不是 RGBA" % (entry["id"], entry["name"], image.mode))
            continue

        sprite = np.asarray(image, dtype=np.uint8)
        if sprite.shape[1] != entry["width"] or sprite.shape[0] != entry["height"]:
            problems.append("[%s] %s 尺寸不符：%s vs %dx%d" % (
                entry["id"], entry["name"], sprite.shape, entry["width"], entry["height"]))
            continue

        reference_path = os.path.join(reference_dir, "%s.rgba" % entry["id"])
        if not os.path.exists(reference_path):
            print("[%s] %s 缺少 psd-tools 参考像素，跳过还原检查" % (entry["id"], entry["name"]))
            continue

        info = json.load(open(os.path.join(reference_dir, "index.json")))
        layer = [item for item in info["layers"] if item["id"] == entry["id"]]
        if not layer:
            print("[%s] %s 参考索引里没有这条，跳过" % (entry["id"], entry["name"]))
            continue

        layer = layer[0]
        reference = np.frombuffer(open(reference_path, "rb").read(), dtype=np.uint8)
        reference = reference.reshape(layer["height"], layer["width"], 4)

        x, y, width, height = entry["sourceRect"]
        x, y, width, height = int(x), int(y), int(width), int(height)
        trimmed = reference[y:y + height, x:x + width]
        if entry["border"] == [0, 0, 0, 0]:
            # 没有九宫的图保持原尺寸导出，直接一一对应
            restored = sprite
        else:
            restored = expand(sprite, entry["border"], width, height)

        if restored.shape != trimmed.shape:
            problems.append("[%s] %s 还原尺寸不符：%s vs %s" % (
                entry["id"], entry["name"], restored.shape, trimmed.shape))
            continue

        diff = np.abs(restored.astype(np.int16) - trimmed.astype(np.int16))
        peak = int(diff.max()) if diff.size else 0
        if peak > 0:
            problems.append("[%s] %s 九宫还原后与图层像素差 %d（超出容差的字节 %d/%d）" % (
                entry["id"], entry["name"], peak, int((diff > 0).sum()), diff.size))

        if entry["border"] != [0, 0, 0, 0]:
            sliced += 1
        print("%-6s %-32s %3dx%-3d border %-18s %s" % (
            entry["id"], entry["name"][:32], entry["width"], entry["height"],
            tuple(entry["border"]), entry["reason"]))

    print()
    print("Sprite %d 个（其中九宫 %d 个），问题 %d 处" % (len(entries), sliced, len(problems)))
    for problem in problems:
        print("问题: " + problem)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
