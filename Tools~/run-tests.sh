#!/usr/bin/env bash
# 一条命令跑完当前仓库能在命令行跑的全部检查。
# 用法：Tools~/run-tests.sh
#
# 依赖：dotnet SDK；装了 Unity 的机器会多做「编辑器脚本编译检查」与「EditMode 测试」两步。
#   指定其他 Unity 安装位置：UNITY_MANAGED_DIR=/path/to/Contents/Managed
#   跳过 Unity EditMode 测试：SKIP_UNITY_TESTS=1 Tools~/run-tests.sh
#   顺带跑真实 PSD 冒烟：PSD2UGUI_SMOKE_PSD=/path/to/xxx.psd Tools~/run-tests.sh
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== 1/5 Core 单元测试（含导出计划与身份映射）=="
dotnet test "Tools~/CoreTests/Psd2Ugui.CoreTests.csproj"

echo
echo "== 2/5 Unity 兼容性编译检查（Core 按 netstandard2.1 编译）=="
dotnet build "Tools~/CoreNetStandard/Psd2Ugui.CoreNetStandard.csproj"

echo
echo "== 3/5 Editor 脚本编译检查（含 Tests，引用 Unity 自带 DLL）=="
UNITY_MANAGED_DIR="${UNITY_MANAGED_DIR:-/Applications/Unity/Unity.app/Contents/Managed}"
if [ -d "$UNITY_MANAGED_DIR" ]; then
  dotnet build "Tools~/EditorCompile/Psd2Ugui.EditorCompile.csproj" -p:UnityManagedDir="$UNITY_MANAGED_DIR"
else
  echo "跳过：没找到 Unity 托管 DLL（$UNITY_MANAGED_DIR）"
fi

echo
echo "== 4/5 命令行解析工具 =="
dotnet build "Tools~/PsdDump/PsdDump.csproj"

echo
echo "== 5/5 Unity EditMode 测试（端到端：PSD → 贴图 → 预制体）=="
if [ "${SKIP_UNITY_TESTS:-0}" = "1" ]; then
  echo "按 SKIP_UNITY_TESTS=1 跳过。"
else
  # dev-project.sh 用 3 表示「环境不满足」（没装 Unity / 无头起不来），
  # 那属于本机情况，不该让整套检查失败；用例真挂了才会是非 0 非 3。
  set +e
  bash "Tools~/dev-project.sh"
  unity_code=$?
  set -e
  if [ "$unity_code" -eq 3 ]; then
    echo "跳过：本机跑不了无头 EditMode 测试（原因见上）。"
  elif [ "$unity_code" -ne 0 ]; then
    echo "Unity EditMode 测试失败，退出码 $unity_code。" >&2
    exit "$unity_code"
  fi
fi

echo
echo "全部通过。"
