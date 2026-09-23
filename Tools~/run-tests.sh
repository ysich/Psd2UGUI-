#!/usr/bin/env bash
# 一条命令跑完当前仓库能脱离 Unity 执行的全部检查。
# 用法：Tools~/run-tests.sh
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== 1/3 Core 单元测试 =="
dotnet test "Tools~/CoreTests/Psd2Ugui.CoreTests.csproj"

echo
echo "== 2/3 Unity 兼容性编译检查（Core 按 netstandard2.1 编译）=="
dotnet build "Tools~/CoreNetStandard/Psd2Ugui.CoreNetStandard.csproj"

echo
echo "== 3/3 命令行解析工具 =="
dotnet build "Tools~/PsdDump/PsdDump.csproj"

echo
echo "全部通过。"
