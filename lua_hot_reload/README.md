# lua_hot_reload — Lua 模块安全热重载

修改 Lua 文件后，自动判定是否可安全热重载（基于 4 条规则排查），通过则经 Unity MCP 执行 in-place patch，使已有实例立即获得新方法，无需重启 Play Mode。

## 工作原理

```
修改 Lua 文件
     │
     ▼
┌──────────────────────────┐
│ Step 1: 确定目标文件       │  有参数直接用 / 无参数 git diff 检测
└──────────────────────────┘
     │
     ▼
┌──────────────────────────┐
│ Step 2: 确认运行时已安装   │  检查 HotReload.txt 是否部署
└──────────────────────────┘
     │
     ▼
┌──────────────────────────┐
│ Step 3: 4 条安全规则检查   │
│  1. Global.txt 注册？     │  ❌ 全局类不可热重载
│  2. 全局类定义模式？       │  ❌ 已有实例不会更新
│  3. 被其他文件 require？   │  ❌ 有依赖方不做级联
│  4. ctor 变更？           │  ❌ 已有实例不会重构造
└──────────────────────────┘
     │
     ▼ 全部通过
┌──────────────────────────┐
│ Step 4: MCP 执行热重载    │  HotReload.Execute(modPath)
└──────────────────────────┘
     │
     ▼
  ✅ patch 完成，已有实例自动生效
```

## 目录结构

```
lua_hot_reload/
├── SKILL.md                          # 技能 Prompt（Claude 加载）
├── install.sh                        # 一键部署脚本
├── README.md                         # 本文件
└── scripts/
    ├── HotReload.txt                 # Lua 热重载核心模块
    └── LuaHotReloadMcpTool.cs        # C# MCP 自定义工具（Unity Editor）
```

## 快速开始

### 1. 部署

将 skill 目录放入目标项目的 `.claude/skills/` 下，然后运行安装脚本：

```bash
bash .claude/skills/lua_hot_reload/install.sh
```

脚本会将运行时文件部署到：
- `Assets/HotRes/Lua/Claude/HotReload.txt`
- `Assets/Claude/Editor/LuaHotReloadMcpTool.cs`

### 2. 适配

`LuaHotReloadMcpTool.cs` 中使用了 `LuaManager.Instance` 和 `EditorDoString` 接口。如果你的项目使用不同的 Lua 运行时入口，需要修改这两处调用（搜索 `ADAPTATION NOTES`）。

### 3. 使用

```bash
# 指定文件路径（相对于 Assets/HotRes/Lua/，不含 .txt 后缀）
/lua_hot_reload RPG/Entity/State/RPGHeroIdle

# 无参数：自动检测本次会话最近修改的 Lua 文件
/lua_hot_reload
```

## 依赖

| 依赖 | 说明 |
|------|------|
| MCP for Unity (`com.mcp4u.mcpforunity`) | 提供 MCP 工具注册框架 |
| XLua（或兼容 Lua 绑定层） | 提供 `LuaManager` 运行时访问 |
| Newtonsoft.Json | MCP 工具参数解析 |
| Unity Editor Play Mode | 热重载仅在 Play Mode 下有效 |

## 核心组件

### HotReload.txt

Lua 热重载核心模块，提供单一 API：

```lua
local HR = require('Claude/HotReload')
local ok, msg = HR.Execute("UI/Hero/UI_Hero_HeroList")
```

**`class.Execute(modPath)`**
- 取出 `package.loaded[modPath]` 旧 module table
- 清除缓存后重新 `require` 获取新 module
- 将新 table 中的 function/field patch 回旧 table（保持引用不变）
- 恢复 `package.loaded` 指向旧 table
- 返回 `boolean success, string message`

安全机制：加载失败时自动恢复旧模块，不会破坏运行时。

### LuaHotReloadMcpTool.cs

注册为 MCP 工具 `lua_hot_reload`，接收 `modPath` 参数，在 Play Mode 下调用 `HotReload.Execute`。

调用方式（由 Claude 自动触发）：
```
mcp__coplay-mcp__execute_custom_tool(
  tool_name="lua_hot_reload",
  parameters={"modPath": "RPG/Entity/State/RPGHeroIdle"}
)
```

## 安全机制

- **4 条前置规则**：在执行前排除不可安全热重载的场景
- **失败回滚**：Lua `require` 失败时自动恢复旧模块到 `package.loaded`
- **Play Mode 校验**：C# 侧确认 Unity 处于 Play Mode 且 LuaManager 已初始化
- **仅 patch function**：旧 table 中的 function 字段会被清除后用新内容覆盖，data 字段保留原值

## 约束

- 仅 Editor Play Mode 下有效
- 不做级联重载：有依赖方直接判定失败
- 不处理 ctor 变更：已有实例的字段不会重新初始化
- patch 仅覆盖 function 类型字段，data 字段原值保留

## 许可

内部工具，仅供项目内部使用。
