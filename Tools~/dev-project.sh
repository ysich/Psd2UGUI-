#!/usr/bin/env bash
# 生成/复用一个宿主 Unity 工程，把本仓库作为「本地包」装进去，然后尽力无头跑 EditMode 测试。
#
# 为什么不是软链仓库根目录：
#   1) Unity 会给包里的每个资产补 .meta 文件，软链回来等于往仓库里灌一堆未跟踪文件；
#   2) 宿主工程本身（Library/Temp/Logs）会落在包目录里，被反向当成包资产扫描。
# 所以脚本把 Editor / Runtime / Tests / package.json 复制到宿主工程内的 stage 目录，
# 让 Unity 去折腾那份副本，仓库本身保持干净。
#
# 用法：
#   Tools~/dev-project.sh                  # 建工程 + 同步包 + 跑 EditMode 测试
#   Tools~/dev-project.sh --sync           # 只同步包内容（改完代码不想重开 Unity 就跑这个）
#   Tools~/dev-project.sh --create-only    # 只准备工程，不跑测试
#   Tools~/dev-project.sh --clean-meta     # 清理仓库里被 Unity 生成的未跟踪 .meta
#
# 可覆盖的环境变量：
#   UNITY=/Applications/Unity/Unity.app/Contents/MacOS/Unity
#   DEV_PROJECT=<仓库>/DevProject
#   PSD2UGUI_SMOKE_PSD=/path/to/xxx.psd   # 配上就多跑一个「真实 PSD 冒烟」用例
#
# 退出码：0 成功；2 有用例失败；3 环境不满足（没装 Unity / 无头起不来 / 用例没真跑起来）。
set -euo pipefail

cd "$(dirname "$0")/.."
REPO="$PWD"

UNITY="${UNITY:-/Applications/Unity/Unity.app/Contents/MacOS/Unity}"
DEV_PROJECT="${DEV_PROJECT:-$REPO/DevProject}"
PACKAGE_NAME="com.sicy.psd2ugui"
STAGE="$DEV_PROJECT/stage/$PACKAGE_NAME"
RESULTS_DIR="$DEV_PROJECT/TestResults"

# Unity 会忽略以 . 或 ~ 开头的目录；下面这几份才是要装进去的资产（外加 package.json）。
PACKAGE_SOURCES=(Editor Runtime Tests)

mode="run"
for arg in "$@"; do
  case "$arg" in
    --sync) mode="sync" ;;
    --create-only) mode="create-only" ;;
    --run|--run-tests) mode="run" ;;
    --clean-meta) mode="clean-meta" ;;
    -h|--help) sed -n '2,21p' "$0"; exit 0 ;;
    *) echo "未知参数：${arg}（-h 看用法）" >&2; exit 64 ;;
  esac
done

# ---------------------------------------------------------------- clean-meta

if [ "$mode" = "clean-meta" ]; then
  # 只有「git 不认识、又是 .meta」的文件才删；真提交过的 .meta 一律不动。
  count=0
  while IFS= read -r file; do
    [ -n "$file" ] || continue
    python3 -c "import os,sys; os.remove(sys.argv[1])" "$file"
    count=$((count + 1))
  done < <(git ls-files --others --exclude-standard -- '*.meta' ':(exclude)DevProject')
  echo "清理了 $count 个未跟踪的 .meta 文件。"
  exit 0
fi

# ---------------------------------------------------------------- 版本探测

unity_version() {
  local plist
  plist="$(dirname "$UNITY")/../Info.plist"
  if [ -f "$plist" ]; then
    plutil -extract CFBundleVersion raw -o - "$plist" 2>/dev/null || true
  fi
}

# 本地包缓存里同一个包可能有多个版本，挑最高的那个，省得写死版本号换机器就挂。
pick_version() {
  local name="$1" fallback="$2" cache="${HOME}/Library/Unity/cache/packages/packages.unity.com"
  local best=""
  if [ -d "$cache" ]; then
    best="$(ls -1 "$cache" 2>/dev/null | sed -n "s/^${name}@//p" | sort -t. -k1,1n -k2,2n -k3,3n | tail -1 || true)"
  fi
  echo "${best:-$fallback}"
}

EDITOR_VERSION="$(unity_version)"
EDITOR_VERSION="${EDITOR_VERSION:-2022.3.62f3}"
TEST_FRAMEWORK="$(pick_version com.unity.test-framework 1.1.33)"
TMP_VERSION="$(pick_version com.unity.textmeshpro 3.0.9)"

# ---------------------------------------------------------------- 建工程

mkdir -p "$DEV_PROJECT/Assets" "$DEV_PROJECT/Packages" "$DEV_PROJECT/ProjectSettings"

cat > "$DEV_PROJECT/ProjectSettings/ProjectVersion.txt" <<EOF
m_EditorVersion: $EDITOR_VERSION
EOF

# TMP 装上才有 TMP 分支可测；缓存里找不到就不写进依赖，避免离线解析失败卡住整个工程。
tmp_dependency=""
if [ -n "$TMP_VERSION" ]; then
  tmp_dependency=",
    \"com.unity.textmeshpro\": \"$TMP_VERSION\""
fi

cat > "$DEV_PROJECT/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.unity.ugui": "1.0.0",
    "com.unity.test-framework": "$TEST_FRAMEWORK",
    "$PACKAGE_NAME": "file:../stage/$PACKAGE_NAME"$tmp_dependency
  },
  "testables": [
    "$PACKAGE_NAME"
  ]
}
EOF

echo "宿主工程：$DEV_PROJECT"
echo "  Unity              $EDITOR_VERSION"
echo "  test-framework     $TEST_FRAMEWORK"
echo "  textmeshpro        ${TMP_VERSION:-（未找到，跳过）}"

# ---------------------------------------------------------------- 同步包内容

# 只带资产目录，不带 .git / Tools~ / docs / DevProject，免得 Unity 去扫自己。
for name in "${PACKAGE_SOURCES[@]}"; do
  python3 - "$REPO" "$STAGE" "$name" <<'PY'
import os, shutil, sys

repo, stage, name = sys.argv[1], sys.argv[2], sys.argv[3]
src, dst = os.path.join(repo, name), os.path.join(stage, name)

# Unity 补出来的 .meta 只存在于 stage 副本里，这里连副本一起重建，保证和仓库一致。
if os.path.isdir(dst):
    shutil.rmtree(dst)
shutil.copytree(src, dst, ignore=shutil.ignore_patterns("bin", "obj"))
PY
done

mkdir -p "$STAGE"
cp "$REPO/package.json" "$STAGE/package.json"
echo "已同步包内容到 $STAGE"

if [ "$mode" != "run" ]; then
  exit 0
fi

# ---------------------------------------------------------------- 跑测试

mkdir -p "$RESULTS_DIR"

if [ ! -x "$UNITY" ]; then
  echo
  echo "跳过 Unity 测试：找不到可执行文件 $UNITY"
  echo "（装好 Unity 后用 UNITY=/path/to/Unity 指过去，或直接开编辑器手动跑 Window > General > Test Runner）"
  exit 3
fi

echo
echo "== Unity EditMode 测试 =="
if [ -n "${PSD2UGUI_SMOKE_PSD:-}" ]; then
  echo "（含真实 PSD 冒烟：${PSD2UGUI_SMOKE_PSD}）"
else
  echo "（跳过真实 PSD 冒烟；设 PSD2UGUI_SMOKE_PSD=<样本.psd> 可带上）"
fi

log="$RESULTS_DIR/unity.log"
results="$RESULTS_DIR/editmode.xml"

# 上一次的结果留着会让「到底跑没跑」看不出来，先删干净。
python3 -c "import os,sys; [os.remove(p) for p in sys.argv[1:] if os.path.exists(p)]" "$results" "$log"

# 注意：这里不能加 -quit。-runTests 要靠 delayCall 启动，-quit 会在那之前就把编辑器关掉，
# 结果是「0 个用例 + 退出码 0」的假成功。-runTests 自己会在跑完后退出。
set +e
"$UNITY" \
  -batchmode -nographics \
  -projectPath "$DEV_PROJECT" \
  -runTests -testPlatform EditMode \
  -testResults "$results" \
  -logFile "$log"
code=$?
set -e

# 退出码不足以说明问题（上面那种假成功就是 0），必须确认结果文件真的落盘了。
if [ ! -s "$results" ]; then
  echo
  echo "没有产出测试结果文件，说明用例没真正跑起来（Unity 退出码 ${code}）。"
  if grep -q "another Unity instance is running" "$log" 2>/dev/null; then
    echo "原因：Unity 的全局单实例/授权锁被另一个 Unity 实例占着。"
    echo "处理：关掉正在运行的 Unity 编辑器后重跑；"
  else
    echo "原因见日志：$log"
  fi
  echo "也可以直接在编辑器里打开 ${DEV_PROJECT}，用 Window > General > Test Runner 手动跑 EditMode。"
  exit 3
fi

python3 - "$results" <<'PY'
import sys, xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
print("用例 %s：通过 %s，失败 %s，跳过 %s，共 %s（耗时 %ss）" % (
    root.get("result"), root.get("passed"), root.get("failed"),
    root.get("skipped"), root.get("total"), root.get("duration")))
for case in root.iter("test-case"):
    if case.get("result") == "Failed":
        print("  失败：%s" % case.get("fullname"))
        for message in case.iter("message"):
            print("        " + (message.text or "").strip().splitlines()[0])
PY

if [ "$code" -eq 0 ]; then
  echo "EditMode 测试全部通过，结果：$results"
  exit 0
fi

echo
echo "Unity 退出码 ${code}，完整日志：$log"
echo "退出码约定：0 全过；2 有用例失败（看 ${results}）；3 运行出错。"
exit "$code"
