#!/usr/bin/env bash
# 一条命令跑完当前仓库能脱离 Unity 执行的全部检查。
# 用法：Tools~/run-tests.sh
#
# 依赖：dotnet SDK；装了 Unity 的机器会多做一步编辑器脚本编译检查。
#       指定其他 Unity 安装位置：UNITY_MANAGED_DIR=/path/to/Contents/Managed
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== 1/4 Core 单元测试（含导出计划与身份映射）=="
dotnet test "Tools~/CoreTests/Psd2Ugui.CoreTests.csproj"

echo
echo "== 2/4 Unity 兼容性编译检查（Core 按 netstandard2.1 编译）=="
dotnet build "Tools~/CoreNetStandard/Psd2Ugui.CoreNetStandard.csproj"

echo
echo "== 3/4 Editor 脚本编译检查（引用 Unity 自带 DLL）=="
UNITY_MANAGED_DIR="${UNITY_MANAGED_DIR:-/Applications/Unity/Unity.app/Contents/Managed}"
if [ -d "$UNITY_MANAGED_DIR" ]; then
  dotnet build "Tools~/EditorCompile/Psd2Ugui.EditorCompile.csproj" -p:UnityManagedDir="$UNITY_MANAGED_DIR"
else
  echo "跳过：没找到 Unity 托管 DLL（$UNITY_MANAGED_DIR）"
fi

echo
echo "== 4/4 命令行解析工具 =="
dotnet build "Tools~/PsdDump/PsdDump.csproj"

echo
echo "全部通过。"
