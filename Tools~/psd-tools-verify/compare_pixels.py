#!/usr/bin/env python3
"""比较两边的图层像素与合成结果。

用法：
    python compare_pixels.py <reference_dir> <actual_dir> [--tolerance 2]

reference_dir 由 dump_pixels.py 生成（psd-tools 侧），actual_dir 由 `PsdDump --pixels` 生成。

三类检查：
  1. 图层元信息（顺序、矩形、不透明度、可见性）一致
  2. 每个公共图层的 RGBA 逐字节一致（容差以内）
  3. 用 reference 侧的图层像素 + 元信息做一次独立的 numpy 合成，和自研解析器的合成结果对照
     （这一项同时验证了图层顺序、不透明度与 alpha 混合公式）

psd-tools 自己合成的画面会烘焙图层阴影/描边等效果，我们的导出器不烘焙效果，
因此那份结果只做参考打印，不计入失败。

退出码 0 表示通过。
"""

import json
import os
import sys

import numpy as np


def parse_args():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    tolerance = 2
    for index, value in enumerate(sys.argv[1:]):
        if value == "--tolerance":
            tolerance = int(sys.argv[index + 2])
    return args, tolerance


def load_rgba(directory, name):
    with open(os.path.join(directory, name), "rb") as handle:
        return handle.read()


def recompose(layers, directory, width, height):
    """按图层顺序自下而上做直通 alpha 合成，与 C# 侧保持同一套公式。"""
    canvas = np.zeros((height, width, 4), dtype=np.uint8)
    for layer in layers:
        if not layer["visible"]:
            continue
        path = os.path.join(directory, "%d.rgba" % layer["id"])
        if not os.path.exists(path):
            continue
        raw = np.frombuffer(open(path, "rb").read(), dtype=np.uint8)
        source = raw.reshape(layer["height"], layer["width"], 4).astype(np.float64) / 255.0
        x, y = layer["x"], layer["y"]
        source_alpha = source[:, :, 3:4] * (layer["opacity"] / 255.0)
        target = canvas[y:y + layer["height"], x:x + layer["width"]].astype(np.float64) / 255.0
        target_alpha = target[:, :, 3:4]
        out_alpha = source_alpha + target_alpha * (1.0 - source_alpha)
        rgb = np.where(
            out_alpha > 0.0,
            (source[:, :, :3] * source_alpha + target[:, :, :3] * target_alpha * (1.0 - source_alpha))
            / np.maximum(out_alpha, 1e-12),
            0.0)
        block = np.clip(np.concatenate([rgb, out_alpha], axis=2), 0.0, 1.0)
        canvas[y:y + layer["height"], x:x + layer["width"]] = np.rint(block * 255.0).astype(np.uint8)
    return canvas


def main():
    args, tolerance = parse_args()
    if len(args) != 2:
        sys.exit(__doc__)

    reference_dir, actual_dir = args
    reference = json.load(open(os.path.join(reference_dir, "index.json")))
    actual = json.load(open(os.path.join(actual_dir, "index.json")))

    problems = []
    if reference["document"] != actual["document"]:
        problems.append("画布尺寸不一致: %s vs %s" % (reference["document"], actual["document"]))

    reference_layers = {layer["id"]: layer for layer in reference["layers"]}
    actual_layers = {layer["id"]: layer for layer in actual["layers"]}
    reference_order = [layer["id"] for layer in reference["layers"]]
    actual_order = [layer["id"] for layer in actual["layers"]]

    if reference_order != actual_order:
        problems.append("图层顺序不一致:\n  psd-tools %s\n  自研      %s" % (reference_order, actual_order))

    missing = [i for i in reference_order if i not in actual_layers]
    extra = [i for i in actual_order if i not in reference_layers]
    if missing:
        problems.append("缺少图层: %s" % [reference_layers[i]["name"] for i in missing])
    if extra:
        problems.append("多出图层: %s" % [actual_layers[i]["name"] for i in extra])

    compared = 0
    worst = 0
    for layer_id in [i for i in reference_order if i in actual_layers]:
        expected = reference_layers[layer_id]
        got = actual_layers[layer_id]
        if (expected["width"], expected["height"], expected["x"], expected["y"]) != (
                got["width"], got["height"], got["x"], got["y"]):
            problems.append("[%d] %s 矩形不一致: %s vs %s" % (layer_id, got["name"], expected, got))
            continue
        if expected["opacity"] != got["opacity"] or expected["visible"] != got["visible"]:
            problems.append("[%d] %s 不透明度/可见性不一致: %s vs %s" % (layer_id, got["name"], expected, got))

        left = np.frombuffer(load_rgba(reference_dir, "%d.rgba" % layer_id), dtype=np.uint8).astype(np.int16)
        right = np.frombuffer(load_rgba(actual_dir, "%d.rgba" % layer_id), dtype=np.uint8).astype(np.int16)
        compared += 1
        if left.shape != right.shape:
            problems.append("[%d] %s 字节数不一致" % (layer_id, got["name"]))
            continue

        diff = np.abs(left - right)
        peak = int(diff.max()) if diff.size else 0
        worst = max(worst, peak)
        if peak > tolerance:
            problems.append("[%d] %s 像素最大差值 %d，超出容差的字节 %d/%d" % (
                layer_id, got["name"], peak, int((diff > tolerance).sum()), diff.size))

    print("图层像素: 比较 %d 个，最大差值 %d（容差 %d）" % (compared, worst, tolerance))

    document = reference["document"]
    expected_canvas = recompose(reference["layers"], reference_dir, document["width"], document["height"])
    actual_canvas = np.frombuffer(
        load_rgba(actual_dir, "composite.rgba"), dtype=np.uint8).reshape(document["height"], document["width"], 4)
    diff = np.abs(expected_canvas.astype(np.int16) - actual_canvas.astype(np.int16))
    peak = int(diff.max()) if diff.size else 0
    print("合成结果: 参考合成 vs 自研合成 最大差值 %d，超出容差的字节 %d/%d" % (
        peak, int((diff > tolerance).sum()), diff.size))
    if peak > tolerance:
        problems.append("合成结果与独立参考合成不一致（最大差值 %d）" % peak)

    if os.path.exists(os.path.join(reference_dir, "composite.rgba")):
        baked = np.frombuffer(
            load_rgba(reference_dir, "composite.rgba"), dtype=np.uint8).reshape(
            document["height"], document["width"], 4)
        diff = np.abs(baked.astype(np.int16) - actual_canvas.astype(np.int16))
        print("参考信息: psd-tools 自身合成（含图层效果）与自研合成的最大差值 %d，超出容差的字节 %d/%d" % (
            int(diff.max()), int((diff > tolerance).sum()), diff.size))

    for problem in problems:
        print("问题: " + problem)
    print("结论: " + ("通过" if not problems else "存在 %d 处问题" % len(problems)))
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
