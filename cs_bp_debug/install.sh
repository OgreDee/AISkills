#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
# cs_bp_debug install - 一键部署 C# 断点调试工具到 Unity 项目
# ═══════════════════════════════════════════════════════════════════════
#
# 用法:
#   ./install.sh [PROJECT_ROOT]
#
# 参数:
#   PROJECT_ROOT  Unity 项目根目录（包含 Assets/ 的目录）
#                 不传则使用当前工作目录
#
# 部署文件:
#   Assets/Plugins/Editor/0Harmony.dll            HarmonyLib 2.3.3
#   Assets/Claude/Editor/CSharpDebugHook.cs       主 API + JSONL 输出
#   Assets/Claude/Editor/HarmonyPatchEngine.cs    Harmony patch 引擎
#   Assets/Claude/Editor/CSharpEvaluator.cs       Layer 2 REPL
#   Assets/Claude/Editor/CSharpDebugMcpTool.cs    MCP 工具接口
#
# 前置依赖:
#   - MCP for Unity (com.coplaydev.unity-mcp)
#   - Newtonsoft.Json (Unity 项目中已包含)
#
# ═══════════════════════════════════════════════════════════════════════

set -euo pipefail

# ─── 颜色 ───
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# ─── 路径解析 ───
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_SRC="$SCRIPT_DIR/scripts"

PROJECT_ROOT="${1:-$(pwd)}"
PROJECT_ROOT="$(cd "$PROJECT_ROOT" && pwd)"

# ─── 校验 ───
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo -e "${CYAN}  cs_bp_debug installer${NC}"
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo ""
echo -e "  项目目录: ${YELLOW}$PROJECT_ROOT${NC}"
echo ""

if [ ! -d "$PROJECT_ROOT/Assets" ]; then
    echo -e "${RED}[ERROR] 未找到 Assets/ 目录，请确认这是一个 Unity 项目根目录${NC}"
    echo "  尝试: ./install.sh /path/to/unity/project"
    exit 1
fi

# ─── 配置目标路径 ───
PLUGINS_DIR="$PROJECT_ROOT/Assets/Plugins/Editor"
EDITOR_DIR="$PROJECT_ROOT/Assets/Claude/Editor"
TEMP_DIR="$PROJECT_ROOT/Temp"

# ─── 创建目录 ───
echo -e "${CYAN}[1/5] 创建目标目录...${NC}"
mkdir -p "$PLUGINS_DIR"
mkdir -p "$EDITOR_DIR"
mkdir -p "$TEMP_DIR"
echo -e "  ${GREEN}✓${NC} $PLUGINS_DIR"
echo -e "  ${GREEN}✓${NC} $EDITOR_DIR"
echo -e "  ${GREEN}✓${NC} $TEMP_DIR"

# ─── 部署 HarmonyLib DLL ───
echo ""
echo -e "${CYAN}[2/5] 部署 0Harmony.dll...${NC}"
DEST_DLL="$PLUGINS_DIR/0Harmony.dll"
if [ -f "$DEST_DLL" ]; then
    echo -e "  ${YELLOW}⚠ 已存在，备份为 0Harmony.dll.bak${NC}"
    cp "$DEST_DLL" "$DEST_DLL.bak"
fi
cp "$SCRIPTS_SRC/0Harmony.dll" "$DEST_DLL"
echo -e "  ${GREEN}✓${NC} → $DEST_DLL"

# ─── 部署 C# 脚本 ───
echo ""
echo -e "${CYAN}[3/5] 部署 C# Editor 脚本...${NC}"

CS_FILES=("CSharpDebugHook.cs" "HarmonyPatchEngine.cs" "CSharpEvaluator.cs" "CSharpDebugMcpTool.cs")
for file in "${CS_FILES[@]}"; do
    DEST="$EDITOR_DIR/$file"
    if [ -f "$DEST" ]; then
        echo -e "  ${YELLOW}⚠ $file 已存在，备份${NC}"
        cp "$DEST" "$DEST.bak"
    fi
    cp "$SCRIPTS_SRC/$file" "$DEST"
    echo -e "  ${GREEN}✓${NC} → $DEST"
done

# ─── 创建 .meta 文件（如果不存在）───
echo ""
echo -e "${CYAN}[4/5] 检查 .meta 文件...${NC}"

# 为 Plugins/Editor 目录创建 meta（如果需要）
if [ ! -f "$PLUGINS_DIR.meta" ]; then
    cat > "$PLUGINS_DIR.meta" << 'METAEOF'
fileFormatVersion: 2
guid: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
METAEOF
    echo -e "  ${GREEN}✓${NC} 创建 Plugins/Editor.meta"
fi

# ─── 初始化 JSONL 输出文件 ───
echo ""
echo -e "${CYAN}[5/5] 初始化 JSONL 输出文件...${NC}"
DEST_JSONL="$TEMP_DIR/cs_debug_capture.jsonl"
if [ ! -f "$DEST_JSONL" ]; then
    touch "$DEST_JSONL"
fi
echo -e "  ${GREEN}✓${NC} → $DEST_JSONL"
echo -e "  ${CYAN}(Temp/ 已被 Unity .gitignore 排除，无需额外配置)${NC}"

# ─── 完成 ───
echo ""
echo -e "${GREEN}═══════════════════════════════════════════${NC}"
echo -e "${GREEN}  部署完成!${NC}"
echo -e "${GREEN}═══════════════════════════════════════════${NC}"
echo ""
echo "  部署的文件:"
echo "    [DLL]    Assets/Plugins/Editor/0Harmony.dll"
echo "    [C#]     Assets/Claude/Editor/CSharpDebugHook.cs"
echo "    [C#]     Assets/Claude/Editor/HarmonyPatchEngine.cs"
echo "    [C#]     Assets/Claude/Editor/CSharpEvaluator.cs"
echo "    [C#]     Assets/Claude/Editor/CSharpDebugMcpTool.cs"
echo "    [Output] Temp/cs_debug_capture.jsonl"
echo ""
echo -e "  ${YELLOW}注意事项:${NC}"
echo "    1. CSharpDebugMcpTool.cs 依赖 MCP for Unity 包"
echo "       如未安装: https://github.com/nicengi/MCP-For-Unity"
echo "    2. 回到 Unity Editor 等待编译完成"
echo "    3. 通过 MCP 调用 csharp_debug 工具进行 C# 断点调试"
echo "    4. Layer 2 (REPL) 依赖 Mono.CSharp，不可用时自动降级"
echo ""
