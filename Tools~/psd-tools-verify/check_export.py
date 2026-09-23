#!/usr/bin/env python3
"""独立校验一次导出产物：契约 JSON + 落盘的 Sprite PNG。

用法：
    python check_export.py <export_dir> [pixel_reference_dir]

- <export_dir> 是 `PsdDump --export-dir`（或 Unity 侧 SpriteExporter）的输出根目录
- [pixel_reference_dir] 由 dump_pixels.py 生成（psd-tools 的图层像素），给了就做逐像素还原检查

检查内容：
  1. 契约里每条 sprite 资源都在 `sprite/<模块>/` 下有对应文件，文件名/尺寸/九宫与契约一致
  2. PNG 能被 Pillow 解码，颜色模式是 RGBA
  3. 节点引用到的资源 ID 全部在资源表里
  4. 给出参考像素时：把 Sprite 按九宫语义拉伸回原尺寸，必须与 psd-tools 的图层像素逐像素一致
     —— 这一条同时验证了九宫边框、中间区最小化与 PNG 编码三件事

退出码 0 表示全部通过。
"""

import glob
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


def walk(node):
    yield node
    for child in node.get("children", []):
        for item in walk(child):
            yield item


def check_contract(export_dir, contract_path, reference_dir, problems, stats):
    contract = json.load(open(contract_path))
    module = contract["document"]["module"]
    print("契约: %s（模块 %s，节点 %d）" % (
        os.path.relpath(contract_path, export_dir), module, int(contract["stats"]["nodes"])))

    resources = {resource["id"]: resource for resource in contract["resources"]}
    for resource in contract["resources"]:
        path = os.path.join(export_dir, resource["path"])
        if not os.path.exists(path):
            problems.append("[%s] 缺文件 %s" % (resource["id"], resource["path"]))
            continue

        if os.path.basename(path) != resource["fileName"]:
            problems.append("[%s] 文件名不符: %s" % (resource["id"], path))

        image = Image.open(path)
        if image.mode != "RGBA":
            problems.append("[%s] %s 颜色模式是 %s，不是 RGBA" % (resource["id"], resource["name"], image.mode))
            continue

        sprite = np.asarray(image, dtype=np.uint8)
        if sprite.shape[1] != resource["width"] or sprite.shape[0] != resource["height"]:
            problems.append("[%s] %s 尺寸不符：%dx%d vs %dx%d" % (
                resource["id"], resource["name"], sprite.shape[1], sprite.shape[0],
                resource["width"], resource["height"]))
            continue

        border = resource["border"]
        box = (border["left"], border["bottom"], border["right"], border["top"])
        if max(box) * 2 >= min(resource["width"], resource["height"]) and max(box) > 0:
            problems.append("[%s] %s 九宫边框 %s 大到没有可拉伸区" % (resource["id"], resource["name"], box))

        restored = None
        if reference_dir:
            reference_path = os.path.join(reference_dir, "%d.rgba" % resource["sourceLayerId"])
            index_path = os.path.join(reference_dir, "index.json")
            if os.path.exists(reference_path) and os.path.exists(index_path):
                reference_index = json.load(open(index_path))
                entry = next((item for item in reference_index["layers"]
                              if item["id"] == resource["sourceLayerId"]), None)
                if entry:
                    reference = np.frombuffer(open(reference_path, "rb").read(), dtype=np.uint8)
                    reference = reference.reshape(entry["height"], entry["width"], 4)
                    rect = resource["sourceRect"]
                    x, y = int(rect["x"]), int(rect["y"])
                    width, height = int(rect["width"]), int(rect["height"])
                    trimmed = reference[y:y + height, x:x + width]
                    restored = sprite if tuple(box) == (0, 0, 0, 0) else expand(sprite, box, width, height)
                    if restored.shape != trimmed.shape:
                        problems.append("[%s] %s 还原尺寸不符：%s vs %s" % (
                            resource["id"], resource["name"], restored.shape, trimmed.shape))
                        restored = None
                    else:
                        diff = np.abs(restored.astype(np.int16) - trimmed.astype(np.int16))
                        peak = int(diff.max()) if diff.size else 0
                        if peak > 0:
                            problems.append("[%s] %s 还原后与图层像素差 %d（%d/%d 字节）" % (
                                resource["id"], resource["name"], peak, int((diff > 0).sum()), diff.size))
                        else:
                            stats["exact"] += 1

        if max(box) > 0:
            stats["sliced"] += 1
        stats["resources"] += 1
        print("  %-20s %-30s %4dx%-4d border %-16s %s" % (
            resource["id"], resource["name"][:30], resource["width"], resource["height"],
            str(box), "共用" if resource["shared"] else ""))

    referenced = set()
    for node in walk(contract["root"]):
        resource_id = node.get("resourceId")
        if resource_id:
            referenced.add(resource_id)
            if resource_id not in resources:
                problems.append("[%s] 节点 %s 引用了不存在的资源 %s" % (node["id"], node["name"], resource_id))

    unused = set(resources) - referenced
    if unused:
        stats["unused"] = len(unused)
    print("  节点引用 %d 个资源；资源表 %d 条" % (len(referenced), len(resources)))
    return contract


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)

    export_dir = sys.argv[1]
    reference_dir = sys.argv[2] if len(sys.argv) > 2 else None

    contracts = sorted(glob.glob(os.path.join(export_dir, "contract", "*", "*.json")))
    if not contracts:
        print("没找到契约 JSON：%s" % os.path.join(export_dir, "contract", "*", "*.json"))
        return 1

    problems = []
    stats = {"resources": 0, "sliced": 0, "exact": 0, "unused": 0}
    for contract_path in contracts:
        check_contract(export_dir, contract_path, reference_dir, problems, stats)

    print()
    print("资源 %d 张（其中九宫 %d 张），逐像素还原一致 %d 张，未被节点引用 %d 张" % (
        stats["resources"], stats["sliced"], stats["exact"], stats["unused"]))
    if reference_dir and stats["exact"] != stats["resources"]:
        print("提示：未做逐像素比对的资源可能是图层带蒙版/裁剪，psd-tools 的 numpy() 不含这些效果")

    for problem in problems:
        print("问题: " + problem)
    print("问题 %d 处" % len(problems))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
