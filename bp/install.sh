#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════
# bp install - 统一入口：依次执行所有依赖 skill 的 install
# ═══════════════════════════════════════════════════════════════════════
#
# 用法:
#   ./install.sh [PROJECT_ROOT]
#
# 参数:
#   PROJECT_ROOT  Unity 项目根目录（包含 Assets/ 的目录）
#                 不传则使用当前工作目录
#
# 依赖 Skill:
#   - lua_bp_debug   (Lua 断点调试)
#   - cs_bp_debug    (C# 断点调试)
#   - lua_hot_reload (Lua 热重载)
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
SKILLS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

PROJECT_ROOT="${1:-$(pwd)}"

# ─── 校验项目目录 ───
if [ ! -d "$PROJECT_ROOT/Assets" ]; then
    echo -e "${RED}[ERROR] 未找到 Assets/ 目录，请确认这是一个 Unity 项目根目录${NC}"
    echo "  尝试: ./install.sh /path/to/unity/project"
    exit 1
fi

PROJECT_ROOT="$(cd "$PROJECT_ROOT" && pwd)"

# ─── 开始 ───
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo -e "${CYAN}  bp installer (统一入口)${NC}"
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo ""
echo -e "  项目目录: ${YELLOW}$PROJECT_ROOT${NC}"
echo ""
echo -e "  将依次安装以下 skill:"
echo -e "    [1] lua_bp_debug"
echo -e "    [2] cs_bp_debug"
echo -e "    [3] lua_hot_reload"
echo ""

# ─── 依次执行依赖 skill 的 install ───
DEPS=("lua_bp_debug" "cs_bp_debug" "lua_hot_reload")
FAILED=()

for dep in "${DEPS[@]}"; do
    INSTALL_SCRIPT="$SKILLS_DIR/$dep/install.sh"
    if [ ! -f "$INSTALL_SCRIPT" ]; then
        echo -e "${RED}[ERROR] 未找到 $dep/install.sh${NC}"
        FAILED+=("$dep")
        continue
    fi

    echo ""
    echo -e "${CYAN}────────────────────────────────────────────${NC}"
    echo -e "${CYAN}  安装 $dep ...${NC}"
    echo -e "${CYAN}────────────────────────────────────────────${NC}"
    echo ""

    if bash "$INSTALL_SCRIPT" "$PROJECT_ROOT"; then
        echo -e "  ${GREEN}✓ $dep 安装成功${NC}"
    else
        echo -e "  ${RED}✗ $dep 安装失败${NC}"
        FAILED+=("$dep")
    fi
done

# ─── 汇总 ───
echo ""
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
if [ ${#FAILED[@]} -eq 0 ]; then
    echo -e "${GREEN}  全部安装完成!${NC}"
else
    echo -e "${YELLOW}  安装完成（有失败项）:${NC}"
    for f in "${FAILED[@]}"; do
        echo -e "    ${RED}✗ $f${NC}"
    done
fi
echo -e "${CYAN}═══════════════════════════════════════════${NC}"
echo ""

[ ${#FAILED[@]} -eq 0 ] || exit 1
