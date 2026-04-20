# bp_debug — AI 自动断点调试技能

基于 MCP (Model Context Protocol) 的 Unity XLua 自动断点调试工具。让 Claude 能够分析 Bug、自动注入断点、捕获运行时数据并定位根因。

## 工作原理

```
Bug 描述 → Claude 分析代码 → 生成断点 → MCP 注入 Unity → Lua Hook 捕获数据 → Claude 诊断根因
```

整个调试过程分为 6 个阶段：

| 阶段 | 内容 |
|------|------|
| Phase 0 | 环境预检（文件、Play Mode、MCP 工具） |
| Phase 1 | Bug 分析与假设 |
| Phase 2 | 静态代码分析 |
| Phase 3 | 断点方案生成 |
| Phase 4 | 注入运行中的 Unity Editor |
| Phase 5-6 | 轮询捕获数据、分析变量状态、输出结论 |

## 目录结构

```
bp_debug/
├── README.md
├── SKILL.md              # 完整技能文档（Skill Prompt）
├── install.sh            # 一键部署脚本
└── scripts/
    ├── DebugHook.txt          # Lua 调试钩子模块
    ├── DebugCaptureWriter.cs  # C# JSONL 写入器
    └── LuaDebugMcpTool.cs     # MCP Tool（执行 Lua 代码）
```

## 快速开始

### 1. 部署到 Unity 项目

```bash
cd /path/to/your/unity/project
bash /path/to/bp_debug/install.sh .
```

脚本会将文件部署到：
- `Assets/HotRes/Lua/Claude/DebugHook.txt`
- `Assets/Claude/Editor/DebugCaptureWriter.cs`
- `Assets/Claude/Editor/LuaDebugMcpTool.cs`

### 2. 适配你的 Lua 绑定

编辑 `LuaDebugMcpTool.cs`，将 `LuaManager.Instance` 和 `EditorDoString` 替换为你项目中实际的 Lua 执行入口。

### 3. 使用

在 Unity Editor 进入 Play Mode 后，通过 Claude（配合 MCP for Unity）描述你的 Bug，Claude 将自动执行调试流程。

## 依赖

| 依赖 | 说明 |
|------|------|
| Unity Editor | 需要处于 Play Mode |
| XLua / ToLua / SLua | Lua 绑定层（需适配） |
| [MCP for Unity](https://github.com/anthropics/mcp-for-unity) | `com.mcp4u.mcpforunity` |
| Newtonsoft.Json | Unity 通常自带 |

## 核心组件

### DebugHook（Lua）

提供断点注册与运行时捕获能力：

```lua
local dbg = require("Claude/DebugHook")

-- 单行断点
dbg.WatchLine("RPG/Entity/EntityHero", 120)

-- 变量为 nil 时触发
dbg.WatchNil("UI/Hero/UI_Hero_Main", 402, "heroBaseConf")

-- 函数入口断点
dbg.WatchCall("RPG/Entity/EntityHero", "TakeDamage")

-- 多断点 + 条件
dbg.WatchMulti({
    {source="RPG/Battle/Skill", line=88, cond={var="hp", op="<=", value=0}},
    {source="RPG/Battle/Skill", line=95},
}, {maxCaptures=3})

-- 停止
dbg.Stop()
```

**条件断点支持的操作符**：`==`, `~=`, `>`, `<`, `>=`, `<=`, `nil`, `notnil`

### DebugCaptureWriter（C#）

Editor-only 的 JSONL 写入器，将捕获数据写入 `Temp/debug_capture.jsonl`，供 Claude 轮询读取。

### LuaDebugMcpTool（C#）

注册为 MCP Tool (`lua_debug`)，接收 Claude 发来的 Lua 代码并在运行时执行。

## 安全机制

- **FPS 保护**：帧率低于 15 持续 30 帧时自动停止 Hook
- **捕获上限**：默认最多 5 次捕获（可配置）
- **表深度限制**：序列化最多 3 层嵌套
- **字段数限制**：每张表最多 30 个字段
- **环形缓冲**：最多 256 条记录，防止内存无限增长
- **协程传播**：自动 patch `coroutine.create/wrap`

## 输出

调试数据通过两个通道输出：

| 通道 | 格式 | 消费者 |
|------|------|--------|
| Unity Console | 格式化文本 (LogWarning) | 开发者 |
| JSONL 文件 | `Temp/debug_capture.jsonl` | Claude |

每条 JSONL 记录包含：`index`, `source`, `line`, `funcName`, `locals`, `upvalues`, `stack`, `timestamp`

## 许可

内部工具，仅供项目内部使用。
