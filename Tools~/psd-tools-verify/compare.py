#!/usr/bin/env python3
"""比较自研解析器与 psd-tools 的图层明细，输出每一处差异。

用法：
    python compare.py <reference.json> <actual.json> [--tolerance 0.002]

退出码 0 表示完全一致。
"""

import json
import sys

TOLERANCE = 0.002


def nearly_equal(left, right):
    if isinstance(left, (int, float)) and isinstance(right, (int, float)):
        return abs(float(left) - float(right)) <= TOLERANCE
    if isinstance(left, list) and isinstance(right, list):
        return len(left) == len(right) and all(nearly_equal(a, b) for a, b in zip(left, right))
    return left == right


def compare_field(name, expected, actual, problems):
    if name in ("font", "align") and (expected in (None, "") and actual in (None, "")):
        return
    if name == "effects" and expected is not None and actual is not None:
        if sorted(expected) != sorted(actual):
            problems.append("effects 期望 %s 实际 %s" % (expected, actual))
        return
    if not nearly_equal(expected, actual):
        problems.append("%s 期望 %r 实际 %r" % (name, expected, actual))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) != 2:
        sys.exit(__doc__)

    reference = {item["layer_id"]: item for item in json.load(open(args[0]))}
    actual = {item["layer_id"]: item for item in json.load(open(args[1]))}

    missing = sorted(set(reference) - set(actual))
    extra = sorted(set(actual) - set(reference))
    if missing:
        print("缺少图层: %s" % missing)
    if extra:
        print("多出图层: %s" % extra)

    mismatches = 0
    for layer_id in sorted(set(reference) & set(actual)):
        expected, got = reference[layer_id], actual[layer_id]
        problems = []
        for field in sorted(set(expected) | set(got)):
            compare_field(field, expected.get(field), got.get(field), problems)
        if problems:
            mismatches += 1
            print("[%s] %s" % (layer_id, got.get("name")))
            for problem in problems:
                print("    " + problem)

    total = len(set(reference) & set(actual))
    print("比较 %d 个节点，差异 %d 个" % (total, mismatches))
    return 1 if (mismatches or missing or extra) else 0


if __name__ == "__main__":
    sys.exit(main())
