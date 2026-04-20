---
name: bp_debug
description: 自动断点调试：分析bug→设断点→用户触发→读取JSONL→分析→迭代，全自动定位Lua根因
---

# Lua 自动断点调试

Claude 根据 bug 描述自动分析代码、设置断点、解析运行时数据、迭代定位根因。用户只需触发 bug。

## 安装 (部署到其他项目)

本 skill 的所有依赖脚本已包含在 `scripts/` 目录中，可一键部署：

```bash
# 部署到当前项目
.claude/skills/bp_debug/install.sh

# 部署到指定项目
.claude/skills/bp_debug/install.sh /path/to/unity/project
```

**部署内容**：

| 文件 | 部署位置 | 说明 |
|------|----------|------|
| `scripts/DebugHook.txt` | `Assets/HotRes/Lua/Utils/DebugHook.txt` | Lua 断点 Hook |
| `scripts/DebugCaptureWriter.cs` | `Assets/Editor/Claude/DebugCaptureWriter.cs` | JSONL 写入器 |
| `scripts/LuaDebugMcpTool.cs` | `Assets/Editor/Claude/LuaDebugMcpTool.cs` | MCP lua_debug 工具 |

**依赖**：
- [MCP for Unity](https://github.com/nicengi/MCP-For-Unity) (LuaDebugMcpTool 依赖)
- XLua 或兼容 Lua 绑定层
- `LuaDebugMcpTool.cs` 中的 `LuaManager` 引用可能需要适配（搜索 `ADAPTATION NOTES`）

## 前置条件

- Unity Editor Play Mode 运行中
- `Assets/HotRes/Lua/Utils/DebugHook.txt` 已存在（v2: crl 组合 hook）
- `Assets/Editor/Claude/DebugCaptureWriter.cs` 已存在
- `Tools > Lua Debug Hook` 窗口可用（`Ctrl+Shift+D`）

## 工作流

<HARD-GATE>
必须严格按以下步骤执行，不得跳步。每一轮迭代最多 3 轮，第 3 轮后必须输出阶段性诊断报告。
</HARD-GATE>

### Phase 0: 环境预检（首次使用时必须执行）

在 Phase 1 之前，并行执行以下检查。任一项失败则停止并提示用户修复。

**检查项**：

| # | 检查 | 方法 | 失败处理 |
|---|------|------|----------|
| 1 | `DebugHook.txt` 存在 | `Glob("**/Utils/DebugHook.txt")` | 提示用户：需要先部署 DebugHook，提供文件路径 |
| 2 | `DebugCaptureWriter.cs` 存在 | `Glob("**/Claude/DebugCaptureWriter.cs")` | 提示用户：需要先部署 C# 侧 JSONL 写入器 |
| 3 | Unity Play Mode 运行中 | `ReadMcpResourceTool(server="coplay-mcp", uri="mcpforunity://editor/state")` 检查 `isPlaying=true` | 提示用户：请先启动游戏（Play Mode） |
| 4 | `lua_debug` MCP 工具可用 | `mcp__coplay-mcp__execute_custom_tool(tool_name="lua_debug", parameters={"code":"return 'ping'"})` | 提示用户：MCP lua_debug 工具不可用，检查 Unity MCP 连接 |
| 5 | 清理旧 JSONL | `Bash: > Temp/debug_capture.jsonl` 截断文件 | 无需失败处理 |

**执行方式**：检查 1-4 并行执行，全部通过后执行检查 5。输出检查结果表格：

```
环境预检：
  [✓] DebugHook.txt
  [✓] DebugCaptureWriter.cs
  [✓] Unity Play Mode
  [✓] lua_debug MCP 工具
  [✓] JSONL 已清理
```

**同一会话内**：如果已通过预检且 Unity 未退出 Play Mode，后续轮次跳过 Phase 0，仅执行清理旧 JSONL（检查 5）。

### Phase 1: 理解 Bug

1. 收集 bug 描述（用户口述 / 堆栈 / 截图）
2. 提取关键词：模块、症状、触发条件
3. 如果有堆栈，解析 source:line:funcName

### Phase 2: 代码分析

1. 用 Grep/Read 定位相关 Lua 源文件
2. 阅读关键函数逻辑，标记可疑行
3. **静态分析优先**：如果仅靠代码就能确定根因，直接输出修复方案，跳过断点调试
4. 如果需要运行时数据，进入 Phase 3

### Phase 3: 生成断点命令

根据分析结果选择断点类型：

**单断点**（最常用）：
```lua
require("Utils/DebugHook").WatchLine("RPG/Entity/EntityHero", 120)
```

**多断点**（多个可疑位置）：
```lua
require("Utils/DebugHook").WatchMulti({
    {source="RPG/Entity/EntityHero", line=120, vars={"self","hp","dmg"}},
    {source="RPG/Entity/EntityHero", line=135},
}, {maxCaptures=5})
```

**Nil 检测**：
```lua
require("Utils/DebugHook").WatchNil("UI/Hero/UI_Hero_Main", 402, "heroBaseConf")
```

**条件断点**（变量满足条件时触发）：
```lua
require("Utils/DebugHook").WatchMulti({
    {source="RPG/Entity/EntityHero", line=120, cond={var="hp", op="<=", value=0}},
})
```

**函数入口**：
```lua
require("Utils/DebugHook").WatchCall("RPG/Entity/EntityHero", "TakeDamage")
```

#### 条件操作符
| op | 含义 |
|----|------|
| `==` | 等于 |
| `~=` | 不等于 |
| `>` `<` `>=` `<=` | 数值比较 |
| `nil` | 变量为 nil |
| `notnil` | 变量不为 nil |

### Phase 4: 注入断点

**自动注入（首选）**：通过 MCP `lua_debug` 工具直接注入到运行中的 Unity：

```
使用 mcp__unity-mcp__lua_debug 工具，参数：
{
  "code": "package.loaded['Utils/DebugHook']=nil; require('Utils/DebugHook').WatchMulti({...})"
}
```

注入前先用 `package.loaded['Utils/DebugHook']=nil` 清除缓存，确保加载最新版本。

**降级方案**：如果 MCP 不可用（Unity 未连接、Play Mode 未开启等），输出命令让用户手动执行：

```
请在 Tools > Lua Debug Hook（Ctrl+Shift+D）中执行：
require("Utils/DebugHook").WatchMulti({...})
```

**注入后**：告知用户断点已设置，给出具体的复现操作建议（不要只说"请触发 bug"），例如：
```
断点已注入：
  [1] RPG/Entity/EntityHero:120 — 监控 self.hp, dmg
  [2] RPG/Entity/EntityHero:135 — 监控所有局部变量

请在游戏中触发 bug（建议：进入战斗让英雄受到一次伤害），触发后不用回复我。
```

### Phase 5: 读取分析结果

**注入断点后立即启动后台轮询**，不要等用户回复"好了"：
1. 用 `Bash` 的 `run_in_background` 轮询 `Temp/debug_capture.jsonl`，每 3 秒检查一次文件是否有新内容（最多等 60 秒）
2. 文件有数据时自动读取并进入分析
3. 超时仍无数据时再提示用户

```bash
# 后台轮询命令示例
for i in $(seq 1 20); do if [ -s Temp/debug_capture.jsonl ]; then echo "CAPTURED"; exit 0; fi; sleep 3; done; echo "TIMEOUT"
```

**数据获取优先级**：

1. **优先读 JSONL 文件**（结构化数据，最可靠）：
   ```
   Read Temp/debug_capture.jsonl
   ```

2. **如果 JSONL 为空，让用户复制 Console 日志**（LuaDebugHookWindow 中的 [DebugHook] 输出）

3. **如果两者都没有**，进入诊断：
   - 断点路径是否匹配？（XLua source 格式：`@path` 或 `[string "path"]`，normalizeSource 均已处理）
   - 代码路径是否走到了断点行？
   - Hook 是否被其他工具覆盖？（LuaProfilerHook）

### Phase 6: 分析与迭代

解析捕获到的变量状态：
1. 检查各变量值是否符合预期
2. 对比代码逻辑，判断哪步出了问题
3. 结合调用栈信息理解执行路径

**如果定位到根因**：
- 输出诊断报告：变量异常值 + 原因分析 + 修复方案
- 如果用户同意，直接修复代码

**如果需要更多数据**（未超 3 轮）：
- 调整断点位置（上下游函数、增加/缩小 watch 范围）
- 回到 Phase 3

**如果 3 轮仍未定位**：
- 输出阶段性报告：已排除的假设 + 已观测到的数据 + 建议方向
- 建议切换策略：code review / 加日志 / 单元测试

## DebugHook API 速查

| API | 用途 | Hook 类型 |
|-----|------|----------|
| `WatchLine(source, line, opts)` | 行断点 | crl |
| `WatchNil(source, line, varName, opts)` | nil 检测 | crl |
| `WatchCall(source, funcName, opts)` | 函数入口 | c |
| `WatchMulti(bpList, opts)` | 多断点并发 | crl |
| `Stop()` | 停止所有 hook | - |
| `FlushBuffer()` | 手动 flush JSONL | - |

### options 参数

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `maxCaptures` | 5 | 最大捕获次数，达到后自动停止 |
| `maxDepth` | 3 | table 序列化深度 |
| `maxFields` | 30 | table 最大字段数 |
| `stackDepth` | 6 | 调用栈捕获深度（前3层全采集+后3层摘要） |
| `vars` | nil | 变量过滤列表，nil=全部 |

### breakpoint 配置（WatchMulti）

| 字段 | 说明 |
|------|------|
| `source` | Lua 文件路径（如 `RPG/Entity/EntityHero`，无需 @ 前缀） |
| `line` | 行号 |
| `vars` | 变量过滤列表 |
| `cond` | 条件 `{var="x", op="==", value=nil}` |
| `maxHits` | 单断点最大命中次数 |
| `stackDepth` | 该断点的调用栈深度 |

## 输出文件

- **JSONL**: `Temp/debug_capture.jsonl`，每行一条 JSON 记录
- **Console**: `[DebugHook]` 前缀的 LogWarning，可视化格式

## 性能约束

- "crl" 组合 hook：call/return 追踪文件（~0.5-1ms/帧），line 仅在有断点文件中触发
- 安全阀：FPS < 15 持续 30 帧 → 自动 kill hook
- 默认 5 次捕获后自动停止
- 协程自动传播 hook（patch coroutine.create/wrap）

## 注意事项

- source 路径使用 require 路径格式：`RPG/Entity/EntityHero`（不带 `@` 前缀，不带 `.txt`）
- debug.sethook 全局唯一，WatchMulti 会先 Stop 前一个 hook
- WatchCall 使用独立的 "c" hook（性能更好，不需要 line 追踪）
- 条件断点使用结构化配置（{var,op,value}），不支持任意表达式
