#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
# lua_bp_debug install - 一键部署 Lua 断点调试工具到 Unity 项目
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
#   Assets/HotRes/Lua/Claude/DebugHook.txt      Lua 断点 Hook
#   Assets/Claude/Editor/DebugCaptureWriter.cs  JSONL 写入器
#   Assets/Claude/Editor/LuaDebugMcpTool.cs     MCP 执行工具
#   .claude/debug_capture.jsonl                 输出文件 (空)
#
# 前置依赖:
#   - MCP for Unity (com.mcp4u.mcpforunity)
#   - XLua (或兼容的 Lua 绑定层)
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
SKILLS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SCRIPTS_SRC="$SCRIPT_DIR/scripts"

PROJECT_ROOT="${1:-$(pwd)}"
PROJECT_ROOT="$(cd "$PROJECT_ROOT" && pwd)"

# ─── 校验 ───
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo -e "${CYAN}  lua_bp_debug installer${NC}"
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
LUA_DIR="$PROJECT_ROOT/Assets/HotRes/Lua/Claude"
EDITOR_DIR="$PROJECT_ROOT/Assets/Claude/Editor"
TEMP_DIR="$PROJECT_ROOT/Temp"

# ─── 创建目录 ───
echo -e "${CYAN}[1/4] 创建目标目录...${NC}"
mkdir -p "$LUA_DIR"
mkdir -p "$EDITOR_DIR"
mkdir -p "$TEMP_DIR"
echo -e "  ${GREEN}✓${NC} $LUA_DIR"
echo -e "  ${GREEN}✓${NC} $EDITOR_DIR"
echo -e "  ${GREEN}✓${NC} $TEMP_DIR"

# ─── 部署 Lua DebugHook ───
echo ""
echo -e "${CYAN}[2/4] 部署 DebugHook.txt...${NC}"
DEST_HOOK="$LUA_DIR/DebugHook.txt"
if [ -f "$DEST_HOOK" ]; then
    echo -e "  ${YELLOW}⚠ 已存在，备份为 DebugHook.txt.bak${NC}"
    cp "$DEST_HOOK" "$DEST_HOOK.bak"
fi
cp "$SCRIPTS_SRC/DebugHook.txt" "$DEST_HOOK"
echo -e "  ${GREEN}✓${NC} → $DEST_HOOK"

# ─── 部署 C# 脚本 ───
echo ""
echo -e "${CYAN}[3/4] 部署 C# Editor 脚本...${NC}"

DEST_WRITER="$EDITOR_DIR/DebugCaptureWriter.cs"
if [ -f "$DEST_WRITER" ]; then
    echo -e "  ${YELLOW}⚠ DebugCaptureWriter.cs 已存在，备份${NC}"
    cp "$DEST_WRITER" "$DEST_WRITER.bak"
fi
cp "$SCRIPTS_SRC/DebugCaptureWriter.cs" "$DEST_WRITER"
echo -e "  ${GREEN}✓${NC} → $DEST_WRITER"

DEST_MCP="$EDITOR_DIR/LuaDebugMcpTool.cs"
if [ -f "$DEST_MCP" ]; then
    echo -e "  ${YELLOW}⚠ LuaDebugMcpTool.cs 已存在，备份${NC}"
    cp "$DEST_MCP" "$DEST_MCP.bak"
fi
cp "$SCRIPTS_SRC/LuaDebugMcpTool.cs" "$DEST_MCP"
echo -e "  ${GREEN}✓${NC} → $DEST_MCP"

# ─── 初始化 JSONL 输出文件 ───
echo ""
echo -e "${CYAN}[4/4] 初始化 JSONL 输出文件...${NC}"
DEST_JSONL="$TEMP_DIR/debug_capture.jsonl"
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
echo "    [Lua]    Assets/HotRes/Lua/Claude/DebugHook.txt"
echo "    [C#]     Assets/Claude/Editor/DebugCaptureWriter.cs"
echo "    [C#]     Assets/Claude/Editor/LuaDebugMcpTool.cs"
echo "    [Output] Temp/debug_capture.jsonl"
echo ""
echo -e "  ${YELLOW}注意事项:${NC}"
echo "    1. LuaDebugMcpTool.cs 依赖 MCP for Unity 包"
echo "       如未安装: https://github.com/nicengi/MCP-For-Unity"
echo "    2. LuaDebugMcpTool.cs 中的 LuaManager 引用可能需要适配"
echo "       搜索 'ADAPTATION NOTES' 查看说明"
echo "    3. 回到 Unity Editor 等待编译完成"
echo "    4. 使用 Ctrl+Shift+D 打开 Lua Debug Hook 窗口"
echo ""
