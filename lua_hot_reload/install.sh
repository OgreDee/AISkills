#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
# lua_hot_reload install - 一键部署 Lua 热重载工具到 Unity 项目
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
#   Assets/HotRes/Lua/Utils/HotReload.txt         Lua 热重载核心逻辑
#   Assets/Editor/Claude/LuaHotReloadMcpTool.cs   MCP 执行工具
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
NC='\033[0m'

# ─── 路径解析 ───
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_SRC="$SCRIPT_DIR/scripts"

PROJECT_ROOT="${1:-$(pwd)}"
PROJECT_ROOT="$(cd "$PROJECT_ROOT" && pwd)"

# ─── 校验 ───
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo -e "${CYAN}  lua_hot_reload installer${NC}"
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
LUA_DIR="$PROJECT_ROOT/Assets/HotRes/Lua/Utils"
EDITOR_DIR="$PROJECT_ROOT/Assets/Editor/Claude"

# ─── 创建目录 ───
echo -e "${CYAN}[1/3] 创建目标目录...${NC}"
mkdir -p "$LUA_DIR"
mkdir -p "$EDITOR_DIR"
echo -e "  ${GREEN}✓${NC} $LUA_DIR"
echo -e "  ${GREEN}✓${NC} $EDITOR_DIR"

# ─── 部署 Lua HotReload ───
echo ""
echo -e "${CYAN}[2/3] 部署 HotReload.txt...${NC}"
DEST_LUA="$LUA_DIR/HotReload.txt"
if [ -f "$DEST_LUA" ]; then
    echo -e "  ${YELLOW}⚠ 已存在，备份为 HotReload.txt.bak${NC}"
    cp "$DEST_LUA" "$DEST_LUA.bak"
fi
cp "$SCRIPTS_SRC/HotReload.txt" "$DEST_LUA"
echo -e "  ${GREEN}✓${NC} → $DEST_LUA"

# ─── 部署 C# MCP 工具 ───
echo ""
echo -e "${CYAN}[3/3] 部署 C# Editor 脚本...${NC}"
DEST_MCP="$EDITOR_DIR/LuaHotReloadMcpTool.cs"
if [ -f "$DEST_MCP" ]; then
    echo -e "  ${YELLOW}⚠ LuaHotReloadMcpTool.cs 已存在，备份${NC}"
    cp "$DEST_MCP" "$DEST_MCP.bak"
fi
cp "$SCRIPTS_SRC/LuaHotReloadMcpTool.cs" "$DEST_MCP"
echo -e "  ${GREEN}✓${NC} → $DEST_MCP"

# ─── 完成 ───
echo ""
echo -e "${GREEN}═══════════════════════════════════════════${NC}"
echo -e "${GREEN}  部署完成!${NC}"
echo -e "${GREEN}═══════════════════════════════════════════${NC}"
echo ""
echo "  部署的文件:"
echo "    [Lua]  Assets/HotRes/Lua/Utils/HotReload.txt"
echo "    [C#]   Assets/Editor/Claude/LuaHotReloadMcpTool.cs"
echo ""
echo -e "  ${YELLOW}注意事项:${NC}"
echo "    1. 依赖 MCP for Unity 包 (com.mcp4u.mcpforunity)"
echo "    2. LuaHotReloadMcpTool.cs 中的 LuaManager 引用可能需要适配"
echo "       搜索 'ADAPTATION NOTES' 查看说明"
echo "    3. 回到 Unity Editor 等待编译完成"
echo "    4. Play Mode 下通过 /lua_hot_reload 触发"
echo ""
