# cs_bp_debug — C# 自动断点调试

通过 HarmonyLib 动态 patch 和 Mono.CSharp REPL，在 Unity Play Mode 下自动拦截 C# 方法、采集运行时数据、执行表达式求值，帮助 Claude 全自动定位 C# 层 bug 根因。用户只需触发 bug。

## 工作原理

```
Claude 分析 bug → 确定目标方法 → MCP 注入 Harmony patch → 用户触发 → 自动采集 JSONL → Claude 分析 → 迭代
```

双层架构：

| 层级 | 技术 | 能力 |
|------|------|------|
| Layer 1 | HarmonyLib Prefix/Postfix/Finalizer | 拦截方法调用，采集参数、实例字段、返回值、调用栈、异常 |
| Layer 2 | Mono.CSharp.Evaluator | 运行时 REPL，支持条件断点（C# 布尔表达式）和任意表达式求值 |

Layer 2 不可用时自动降级为 Layer 1 + 简单字段比较。

## 目录结构

```
cs_bp_debug/
├── SKILL.md                          # 技能 Prompt（Claude 自动加载）
├── install.sh                        # 一键部署脚本
├── README.md                         # 本文档
├── doc/
│   └── hybrid_harmony_repl.md        # 技术方案设计文档
└── scripts/
    ├── 0Harmony.dll                  # HarmonyLib 2.3.3 (MIT)
    ├── CSharpDebugHook.cs            # 主 API：WatchMethod/WatchMethodEx/Eval/Stop
    ├── HarmonyPatchEngine.cs         # Harmony 动态 patch 引擎 + 序列化
    ├── CSharpEvaluator.cs            # Layer 2 Mono.CSharp REPL 封装
    └── CSharpDebugMcpTool.cs         # MCP 工具接口（csharp_debug）
```

## 快速开始

### 1. 部署

```bash
# 部署到当前 Unity 项目
.claude/skills/cs_bp_debug/install.sh

# 部署到指定项目
.claude/skills/cs_bp_debug/install.sh /path/to/unity/project
```

部署后文件位置：

| 文件 | 部署位置 |
|------|----------|
| `0Harmony.dll` | `Assets/Plugins/Editor/0Harmony.dll` |
| `CSharpDebugHook.cs` | `Assets/Claude/Editor/CSharpDebugHook.cs` |
| `HarmonyPatchEngine.cs` | `Assets/Claude/Editor/HarmonyPatchEngine.cs` |
| `CSharpEvaluator.cs` | `Assets/Claude/Editor/CSharpEvaluator.cs` |
| `CSharpDebugMcpTool.cs` | `Assets/Claude/Editor/CSharpDebugMcpTool.cs` |

### 2. 使用

1. 启动 Unity Editor 并进入 **Play Mode**
2. 向 Claude 描述 C# 层 bug（症状、堆栈、触发条件）
3. Claude 自动分析代码 → 注入断点 → 提示你触发 bug
4. 触发后 Claude 读取 JSONL 数据并分析，最多迭代 3 轮

## 依赖

| 依赖 | 说明 |
|------|------|
| [MCP for Unity](https://github.com/nicengi/MCP-For-Unity) | CSharpDebugMcpTool 依赖，提供 MCP 通信能力 |
| HarmonyLib 2.3.3 | 运行时方法 patch，已包含在 `scripts/0Harmony.dll` |
| Newtonsoft.Json | JSON 序列化，Unity 项目通常已包含 |
| Mono.CSharp（可选） | Layer 2 REPL 求值，不可用时自动降级 |

## 核心组件

### CSharpDebugHook — 主 API

| 方法 | 说明 |
|------|------|
| `WatchMethod(type, method, opts)` | Layer 1 基础监听：采集参数、字段、返回值、调用栈 |
| `WatchMethodEx(type, method, opts)` | Layer 1+2 增强监听：支持条件断点和表达式求值 |
| `Eval(expression)` | Layer 2 即时求值（不设断点） |
| `Stop()` | 停止所有 patch，关闭输出 |
| `GetStatus()` | 查询活跃 patch 和捕获数 |

### MCP 命令速查（csharp_debug 工具）

| action | 关键参数 | 说明 |
|--------|----------|------|
| `watchMethod` | type, method, vars?, maxCaptures? | 基础监听 |
| `watchMethodEx` | type, method, condition?, eval? | 增强监听 |
| `eval` | expression | 即时求值 |
| `stop` | — | 停止所有 patch |
| `status` | — | 查询状态 |

### 输出格式

JSONL 输出到 `Temp/cs_debug_capture.jsonl`，每行一条记录：

- **prefix**（方法进入）：参数、实例字段、表达式求值结果、调用栈
- **postfix**（方法退出）：返回值
- **finalizer**（异常）：异常类型、消息、堆栈

## 安全机制

- **安全阀**：达到 `maxCaptures`（默认 10）后自动 unpatch，防止无限采集
- **零全局影响**：Harmony patch 仅影响被 patch 的方法，不影响其他代码
- **热插拔**：patch 即时生效，`stop` 命令即时移除，无需重启
- **Layer 2 降级**：Mono.CSharp 不可用时自动退化为简单字段比较，不会报错
- **仅 Editor**：所有代码包裹在 `#if UNITY_EDITOR` 中，不进入构建

## 与 Lua 版 (lua_bp_debug) 的差异

| 特性 | lua_bp_debug (Lua) | cs_bp_debug (C#) |
|------|----------------|------------------|
| 拦截机制 | debug.sethook | HarmonyLib Prefix/Postfix |
| 条件断点 | 结构化 {var,op,value} | C# 表达式 (Layer 2) |
| 表达式求值 | 不支持 | Mono.CSharp REPL |
| 性能影响 | 中（line hook 每行触发） | 低（仅方法入口/出口） |
| 输出文件 | `Temp/debug_capture.jsonl` | `Temp/cs_debug_capture.jsonl` |
| MCP 工具 | `lua_debug` | `csharp_debug` |
| 适用场景 | Lua 业务逻辑 bug | C# 引擎层 / 确定性计算 bug |

## 许可

内部工具，仅供项目内部使用。HarmonyLib 使用 MIT 许可证。
