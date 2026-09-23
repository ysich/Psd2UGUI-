#!/usr/bin/env python3
"""用 psd-tools 解析 PSD，输出与 PsdDump --layers 完全同构的 JSON。

这是解析器的“对照组”：两边的字段一一对应，diff 为空才说明自研解析器可信。
需要 psd-tools（仅用于开发期校验，不是插件的运行时依赖）：

    python3 -m venv /tmp/psdenv
    /tmp/psdenv/bin/pip install psd-tools

用法：
    python dump_reference.py <file.psd> <out.json>
"""

import json
import sys

from psd_tools.psd import PSD
from psd_tools.psd.engine_data import EngineData  # noqa: F401  (保持导入以便将来按需使用)

# 效果 classID -> 契约里的种类名，必须与 Core 的 PsdEffectsReader.MapKind 一致
EFFECT_KINDS = {
    b"DrSh": "drop-shadow",
    b"IrSh": "inner-shadow",
    b"OrGl": "outer-glow",
    b"IrGl": "inner-glow",
    b"ebbl": "bevel",
    b"SoFi": "solid-fill",
    b"GrFl": "gradient-fill",
    b"FrFX": "stroke",
    b"ChFX": "satin",
    b"patternFill": "pattern-fill",
}

ALIGNMENTS = {0: "left", 1: "right", 2: "center"}


def plain(value):
    """psd-tools 的值对象统一取出底层 Python 值。"""
    return getattr(value, "value", value)


def effect_kinds(descriptor):
    """按 classID 归类效果，只有 enab 与 present 都为真才算生效。"""
    kinds = []
    if descriptor is None:
        return kinds

    for key in descriptor.keys():
        value = descriptor.get(key)
        if key.endswith(b"Multi"):
            items = list(value) if isinstance(value, (list, tuple)) else [value]
        else:
            items = [value]

        for item in items:
            if item is None or not hasattr(item, "classID") or item.classID is None:
                continue

            kind = EFFECT_KINDS.get(bytes(item.classID))
            if kind is None:
                continue

            def flag(name):
                raw = item.get(name)
                return bool(plain(raw)) if raw is not None else True

            if not flag(b"enab") or not flag(b"present"):
                continue

            if kind not in kinds:
                kinds.append(kind)

    return kinds


def solid_fill(tagged_blocks):
    soco = tagged_blocks.get_data(b"SoCo")
    if soco is None:
        return None

    color = soco.get(b"Clr ")
    if color is None:
        return None

    channels = [plain(color.get(name)) for name in (b"Rd  ", b"Grn ", b"Bl  ")]
    if any(channel is None for channel in channels):
        return None

    return "#%02x%02x%02x%02x" % (
        round(channels[0]),
        round(channels[1]),
        round(channels[2]),
        255,
    )


def text_info(tagged_blocks):
    typed = tagged_blocks.get_data(b"TySh")
    if typed is None:
        return {}

    text_data = typed.text_data
    raw_text = text_data.get(b"Txt ")
    result = {"text": str(plain(raw_text)).rstrip("\x00\r\n") if raw_text is not None else None}

    engine = text_data.get(b"EngineData")
    if engine is None:
        return result

    data = plain(engine)
    style = data["EngineDict"]["StyleRun"]["RunArray"][0]["StyleSheet"]["StyleSheetData"]
    paragraph = data["EngineDict"]["ParagraphRun"]["RunArray"][0]["ParagraphSheet"]["Properties"]

    result["size"] = float(plain(style.get("FontSize"))) if style.get("FontSize") is not None else None

    values = (style.get("FillColor") or {}).get("Values")
    result["color"] = [float(plain(v)) for v in values] if values is not None else None

    fonts = data["ResourceDict"].get("FontSet")
    index = style.get("Font")
    index = int(plain(index)) if index is not None else -1
    if fonts is not None and 0 <= index < len(fonts):
        name = fonts[index].get("Name")
        result["font"] = str(plain(name)) if name is not None else None
    else:
        result["font"] = None

    result["align"] = ALIGNMENTS.get(float(plain(paragraph.get("Justification")) or 0), "left")
    return result


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)

    path, out_path = sys.argv[1], sys.argv[2]
    psd = PSD.frombytes(open(path, "rb").read())
    records = psd.layer_and_mask_information.layer_info.layer_records

    out = []
    for record in records:
        blocks = record.tagged_blocks
        divider = blocks.get_data(b"lsct")

        # type 3 的分隔符只是分组的收尾标记，不产生节点
        if divider is not None and int(divider.kind) == 3:
            continue

        item = {
            "name": str(record.name),
            "layer_id": plain(blocks.get_data(b"lyid")),
            "rect": [record.top, record.left, record.bottom, record.right],
            "opacity": record.opacity,
            "visible": bool(record.flags.visible),
            "clip": int(record.clipping),
            "lsct": int(divider.kind) if divider is not None else None,
            "effects": effect_kinds(blocks.get_data(b"lfx2")),
            "fill": solid_fill(blocks),
            "text": None,
            "font": None,
            "size": None,
            "align": None,
            "color": None,
        }
        item.update(text_info(blocks))
        out.append(item)

    json.dump(out, open(out_path, "w"), ensure_ascii=False, indent=2)
    print("wrote %s (%d nodes, %d records)" % (out_path, len(out), len(records)))


if __name__ == "__main__":
    main()
