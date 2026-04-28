---
name: cs_bp_debug
description: C#自动断点调试：分析bug→设断点→用户触发→读取JSONL→分析→迭代，全自动定位C#根因
tags: [debug, csharp, unity, harmony]
---

# C# 自动断点调试

Claude 根据 bug 描述自动分析 C# 代码、通过 Harmony 设置运行时 patch、解析捕获的运行时数据、迭代定位根因。用户只需触发 bug。

**双层架构**：
- **Layer 1**: HarmonyLib Prefix/Postfix 拦截目标方法，采集参数/字段/返回值/调用栈
- **Layer 2**: Mono.CSharp.Evaluator 运行时 REPL，支持复杂条件断点和表达式求值

## 安装 (部署到其他项目)

```bash
# 部署到当前项目
.claude/skills/cs_bp_debug/install.sh

# 部署到指定项目
.claude/skills/cs_bp_debug/install.sh /path/to/unity/project
```

**部署内容**：

| 文件 | 部署位置 | 说明 |
|------|----------|------|
| `scripts/0Harmony.dll` | `Assets/Plugins/Editor/0Harmony.dll` | HarmonyLib 2.3.3 (MIT) |
| `scripts/CSharpDebugHook.cs` | `Assets/Claude/Editor/CSharpDebugHook.cs` | 主 API + JSONL 输出 |
| `scripts/HarmonyPatchEngine.cs` | `Assets/Claude/Editor/HarmonyPatchEngine.cs` | Harmony 动态 patch 引擎 |
| `scripts/CSharpEvaluator.cs` | `Assets/Claude/Editor/CSharpEvaluator.cs` | Layer 2 REPL |
| `scripts/CSharpDebugMcpTool.cs` | `Assets/Claude/Editor/CSharpDebugMcpTool.cs` | MCP 工具接口 |

**依赖**：
- [MCP for Unity](https://github.com/nicengi/MCP-For-Unity) (CSharpDebugMcpTool 依赖)
- HarmonyLib 2.x (已包含在 scripts/ 中)
- Newtonsoft.Json (项目已有)

## 前置条件

- Unity Editor Play Mode 运行中
- `Assets/Plugins/Editor/0Harmony.dll` 已存在
- `Assets/Claude/Editor/CSharpDebugHook.cs` 等 4 个 C# 文件已存在
- MCP `csharp_debug` 工具可用

## 工作流

<HARD-GATE>
必须严格按以下步骤执行，不得跳步。每一轮迭代最多 3 轮，第 3 轮后必须输出阶段性诊断报告。
</HARD-GATE>

### Phase 0: 环境预检（首次使用时必须执行）

在 Phase 1 之前，并行执行以下检查。任一项失败则停止并提示用户修复。

**检查项**：

| # | 检查 | 方法 | 失败处理 |
|---|------|------|----------|
| 1 | `0Harmony.dll` 存在 | `Glob("**/Plugins/Editor/0Harmony.dll")` | 提示用户运行 install.sh |
| 2 | `CSharpDebugHook.cs` 存在 | `Glob("**/Claude/Editor/CSharpDebugHook.cs")` | 提示用户运行 install.sh |
| 3 | Unity Play Mode 运行中 | `ReadMcpResourceTool(server="coplay-mcp", uri="mcpforunity://editor/state")` 检查 `isPlaying=true` | 提示用户：请先启动游戏（Play Mode） |
| 4 | `csharp_debug` MCP 工具可用 | `mcp__coplay-mcp__execute_custom_tool(tool_name="csharp_debug", parameters={"action":"status"})` | 提示用户：MCP csharp_debug 工具不可用，检查 Unity MCP 连接 |
| 5 | 清理旧 JSONL | `Bash: > Temp/cs_debug_capture.jsonl` 截断文件 | 无需失败处理 |

**执行方式**：检查 1-4 并行执行，全部通过后执行检查 5。输出检查结果表格：

```
环境预检：
  [✓] 0Harmony.dll
  [✓] CSharpDebugHook.cs
  [✓] Unity Play Mode
  [✓] csharp_debug MCP 工具
  [✓] JSONL 已清理
```

**同一会话内**：如果已通过预检且 Unity 未退出 Play Mode，后续轮次跳过 Phase 0，仅执行清理旧 JSONL（检查 5）。

### Phase 1: 理解 Bug

1. 收集 bug 描述（用户口述 / 堆栈 / 截图）
2. 提取关键词：模块、症状、触发条件
3. 如果有堆栈，解析 `Type.Method` 及行号
4. **判断 C# 还是 Lua**：如果是 Lua 层 bug，建议切换到 `lua_bp_debug` skill

### Phase 2: 代码分析

1. 用 Grep/Read 定位相关 C# 源文件
2. 阅读关键方法逻辑，标记可疑位置
3. **静态分析优先**：如果仅靠代码就能确定根因，直接输出修复方案，跳过断点调试
4. 如果需要运行时数据，进入 Phase 3

### Phase 3: 生成断点命令

根据分析结果选择断点类型：

**基础监听（Layer 1）**：
```json
{
  "action": "watchMethod",
  "type": "RPGBattleModule.BattleUnit",
  "method": "TakeDamage",
  "vars": ["hp", "atk", "def"],
  "maxCaptures": 5
}
```

**增强监听（Layer 1+2）**：
```json
{
  "action": "watchMethodEx",
  "type": "RPGBattleModule.BattleUnit",
  "method": "TakeDamage",
  "condition": "hp <= 0",
  "eval": ["__instance.GetType().Name", "__args[0]"],
  "maxCaptures": 10
}
```

**即时求值（Layer 2）**：
```json
{
  "action": "eval",
  "expression": "UnityEngine.Object.FindObjectsOfType<Camera>().Length"
}
```

### Phase 4: 注入断点

**自动注入（首选）**：通过 MCP `csharp_debug` 工具直接注入：

```
使用 mcp__coplay-mcp__execute_custom_tool 工具，参数：
{
  "tool_name": "csharp_debug",
  "parameters": {
    "action": "watchMethod",
    "type": "...",
    "method": "...",
    "vars": [...]
  }
}
```

**注入后**：告知用户断点已设置，给出具体的复现操作建议：
```
断点已注入：
  [1] RPGBattleModule.BattleUnit.TakeDamage — 监控 hp, atk, def (max=5)

请在游戏中触发 bug（建议：进入战斗让英雄受到一次伤害），触发后告诉我。
```

### Phase 5: 读取分析结果

**注入断点后立即启动后台轮询**，不要等用户回复"好了"：
1. 用 `Bash` 的 `run_in_background` 轮询 `Temp/cs_debug_capture.jsonl`，每 3 秒检查一次（最多 60 秒）
2. 文件有数据时自动读取并进入分析
3. 超时仍无数据时再提示用户

```bash
# 后台轮询命令示例
for i in $(seq 1 20); do if [ -s Temp/cs_debug_capture.jsonl ]; then echo "CAPTURED"; exit 0; fi; sleep 3; done; echo "TIMEOUT"
```

**数据获取**：
```
Read Temp/cs_debug_capture.jsonl
```

**如果 JSONL 为空**，进入诊断：
- 类型名是否正确？（需要完整命名空间，如 `RPGBattleModule.BattleUnit`）
- 方法名拼写是否正确？是否为 public/private？
- 代码路径是否走到了目标方法？
- 是否有编译错误导致 patch 失败？

### Phase 6: 分析与迭代

解析捕获到的运行时数据：
1. 检查参数值是否符合预期
2. 检查实例字段状态
3. 对比返回值和预期
4. 结合调用栈理解执行路径

**如果定位到根因**：
- 输出诊断报告：变量异常值 + 原因分析 + 修复方案
- 发送 `{"action":"stop"}` 清理 patch
- 如果用户同意，直接修复代码

**如果需要更多数据**（未超 3 轮）：
- 调整监听目标（上下游方法、增加/缩小 watch 范围）
- 回到 Phase 3

**如果 3 轮仍未定位**：
- 发送 `{"action":"stop"}` 清理 patch
- 输出阶段性报告：已排除的假设 + 已观测到的数据 + 建议方向
- 建议切换策略：code review / 加日志 / 单元测试

## MCP 命令速查

| action | 参数 | 说明 |
|--------|------|------|
| `watchMethod` | type, method, vars?, maxCaptures?, maxDepth?, maxFields?, stackDepth? | Layer 1 基础监听 |
| `watchMethodEx` | type, method, condition?, eval?, vars?, maxCaptures?, ... | Layer 1+2 增强监听 |
| `eval` | expression | Layer 2 即时求值 |
| `stop` | — | 停止所有 patch |
| `status` | — | 查询状态 |

### watchMethod / watchMethodEx 参数

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `type` | 必填 | 完整类型名（含命名空间），如 `RPGBattleModule.BattleUnit` |
| `method` | 必填 | 方法名 |
| `maxCaptures` | 10 | 最大捕获次数，达到后自动 unpatch |
| `maxDepth` | 3 | 对象序列化深度 |
| `maxFields` | 30 | 对象最大字段数 |
| `stackDepth` | 8 | 调用栈捕获深度 |
| `vars` | null | 实例字段过滤列表，null=全部 public+private |

### watchMethodEx 额外参数

| 字段 | 说明 |
|------|------|
| `condition` | C# 布尔表达式，仅满足条件时捕获。可用 `__instance`, `__args`。如果 Layer 2 不可用，退化为简单字段比较（格式：`fieldName op value`） |
| `eval` | C# 表达式数组，每次捕获时求值并记录结果 |

## 输出文件

- **JSONL**: `Temp/cs_debug_capture.jsonl`，每行一条 JSON 记录（与 Lua 版 `debug_capture.jsonl` 分开）
- **Console**: `[CSharpDebug]` 前缀的 Log，可视化格式

### JSONL 格式

**Prefix (方法进入)**：
```json
{"index":1,"type":"prefix","method":"BattleUnit.TakeDamage","phase":"enter","params":{"damage":100,"type":"Physical"},"instance":{"_type":"BattleUnit","hp":500,"def":30},"stack":["BattleManager.ProcessTurn at ..."],"timestamp":"2024-01-01T00:00:00Z"}
```

**Postfix (方法退出)**：
```json
{"index":1,"type":"postfix","method":"BattleUnit.TakeDamage","phase":"exit","result":70,"timestamp":"2024-01-01T00:00:00Z"}
```

**Finalizer (异常)**：
```json
{"index":1,"type":"finalizer","method":"BattleUnit.TakeDamage","phase":"exception","exception":{"type":"NullReferenceException","message":"...","stackTrace":"..."},"timestamp":"2024-01-01T00:00:00Z"}
```

## 性能约束

- Harmony patch 仅影响被 patch 的方法，无全局性能损耗
- 安全阀：达到 `maxCaptures` 后自动 unpatch，防止无限采集
- 默认 10 次捕获后自动停止
- Layer 2 REPL 求值有额外开销，复杂表达式可能影响帧率
- 建议在调试时关闭不必要的 patch（`stop` 命令）

## 与 Lua 版 (lua_bp_debug) 的差异

| 特性 | lua_bp_debug (Lua) | cs_bp_debug (C#) |
|------|----------------|------------------|
| 拦截机制 | debug.sethook | HarmonyLib Prefix/Postfix |
| 条件断点 | 结构化 {var,op,value} | C# 表达式 (Layer 2) 或简单字段比较 (降级) |
| 表达式求值 | 不支持 | Mono.CSharp REPL |
| 全局影响 | debug hook 影响所有 Lua 代码 | 仅影响被 patch 的方法 |
| 输出文件 | `Temp/debug_capture.jsonl` | `Temp/cs_debug_capture.jsonl` |
| MCP 工具 | `lua_debug` | `csharp_debug` |
| 适用场景 | Lua 业务逻辑 bug | C# 引擎层 / 确定性计算 bug |

## 注意事项

- type 参数需要完整命名空间：`RPGBattleModule.BattleUnit`（不是 `BattleUnit`）
- 如果找不到类型，会搜索所有已加载程序集（包括热更新 DLL）
- Harmony patch 不支持泛型方法的直接 patch（需要指定具体泛型实例）
- 每次 `watchMethod` / `watchMethodEx` 会覆盖同一方法的旧 patch
- `stop` 命令会移除所有 patch 并关闭输出文件
