# 方案 10: Hybrid Approach — Harmony Patching + C# REPL

> **专家**: 资深 Unity 工程负责人（务实整合方向）

## 1. 方案概述

**一句话**: 分两层能力——Layer 1 用 Harmony Prefix/Postfix 拦截目标方法采集参数/字段/返回值（方法级），Layer 2 集成 Mono.CSharp.Evaluator 作为运行时 C# REPL（表达式级），在 Harmony 回调中执行任意 C# 表达式检查深层状态。这是"够用就好"的务实方案。

```
┌──────────────────────────────────────────────────────────┐
│                      Claude (AI)                          │
│  Phase 2: 代码分析 → 确定目标方法 + 采集表达式           │
│  Phase 3: 生成 MCP 命令                                  │
│  Phase 5: 读取 JSONL 分析                                │
└───────┬──────────────────────────────────▲────────────────┘
        │ MCP                              │ JSONL
        ▼                                  │
┌──────────────────────────────────────────────────────────┐
│              CSharpDebugMcpTool (MCP 接口)                │
│  csharp_debug tool — JSON 命令驱动                        │
└───────┬──────────────────────────────────────────────────┘
        │
        ▼
┌──────────────────────────────────────────────────────────┐
│                CSharpDebugHook (主控)                      │
│                                                           │
│  ┌─── Layer 1: Method-level Capture ───────────────────┐ │
│  │                                                      │ │
│  │  HarmonyPatchEngine                                  │ │
│  │  ┌────────┐  ┌─────────┐  ┌──────────┐              │ │
│  │  │Prefix  │  │Postfix  │  │Finalizer │              │ │
│  │  │参数    │  │返回值   │  │异常      │              │ │
│  │  │this字段│  │this字段 │  │堆栈      │              │ │
│  │  │调用栈  │  │         │  │          │              │ │
│  │  └────────┘  └─────────┘  └──────────┘              │ │
│  └──────────────────────────────────────────────────────┘ │
│                                                           │
│  ┌─── Layer 2: Expression-level Eval ──────────────────┐ │
│  │                                                      │ │
│  │  CSharpEvaluator (Mono.CSharp.Evaluator)            │ │
│  │  - 条件断点: "damage > this.shield"                  │ │
│  │  - 深度检查: "this.buffs.Where(b=>b.IsActive)"      │ │
│  │  - 自定义采集: "GetComponent<Rigidbody>().velocity" │ │
│  │                                                      │ │
│  └──────────────────────────────────────────────────────┘ │
│                           │                               │
│                           ▼                               │
│              DebugCaptureWriter → JSONL                   │
└──────────────────────────────────────────────────────────┘
```

## 2. 技术原理

### 2.1 Layer 1: Harmony（已在方案01详述）

快速回顾：
- Prefix: 方法前采集参数 + this 字段 + 调用栈
- Postfix: 方法后采集返回值 + 最终状态
- Finalizer: 异常时采集 exception + 堆栈

### 2.2 Layer 2: Mono.CSharp.Evaluator

Unity 的 Mono 运行时内置了交互式 C# 编译器：

```csharp
using Mono.CSharp;

// 初始化 Evaluator
var settings = new CompilerSettings();
var printer = new ConsoleReportPrinter();
var context = new CompilerContext(settings, printer);
var evaluator = new Evaluator(context);

// 导入命名空间
evaluator.Run("using UnityEngine;");
evaluator.Run("using System.Linq;");

// 在 Harmony 回调中使用
// 将当前 instance 和参数注入到 evaluator 上下文
evaluator.Run($"var __instance = ({typeName})__ref;");
object result = evaluator.Evaluate("__instance.buffs.Count(b => b.IsActive)");
```

### 2.3 两层配合模式

```
Claude 命令: WatchMethod("BattleUnit", "TakeDamage", {
    vars: ["damage", "this.hp", "this.shield"],         // Layer 1: Harmony 直接采集
    eval: ["this.buffs.Where(b=>b.type==BuffType.Shield).Sum(b=>b.value)"],  // Layer 2: REPL
    cond: "damage > this.shield"                         // Layer 2: 条件判断
})
```

执行流程：
1. Harmony Prefix 触发
2. Layer 1 直接采集 vars 列表的变量值（高性能）
3. Layer 2 求值 cond 条件，不满足则跳过
4. Layer 2 求值 eval 表达式列表
5. 合并所有数据写入 JSONL

## 3. 核心组件设计

### 3.1 CSharpDebugHook.cs — 主 API

```csharp
public static class CSharpDebugHook
{
    /// Layer 1: 方法级监听
    public static void WatchMethod(string typeName, string methodName, WatchOptions opts = null);

    /// Layer 1+2: 方法级 + 表达式
    public static void WatchMethodEx(string typeName, string methodName, WatchExOptions opts);

    /// Layer 2: 纯表达式求值（不设断点，立即执行）
    public static object Eval(string expression);

    /// 停止所有
    public static void Stop();

    public static bool IsActive();
}

public class WatchOptions
{
    public int MaxCaptures = 5;
    public int MaxDepth = 3;
    public int MaxFields = 30;
    public int StackDepth = 8;
    public string[] Vars;              // Layer 1 直接采集
}

public class WatchExOptions : WatchOptions
{
    public string Condition;            // Layer 2 条件表达式
    public string[] EvalExpressions;    // Layer 2 求值表达式
}
```

### 3.2 HarmonyPatchEngine.cs

```csharp
internal class HarmonyPatchEngine
{
    private Harmony _harmony = new Harmony("com.claude.csharp-debug");
    private Dictionary<string, PatchInfo> _patches = new();

    public void Patch(string typeName, string methodName, CaptureConfig config)
    {
        var targetType = TypeResolver.Resolve(typeName);
        var targetMethod = targetType.GetMethod(methodName, ...);

        _harmony.Patch(targetMethod,
            prefix: GeneratePrefix(config),
            postfix: GeneratePostfix(config),
            finalizer: GenerateFinalizer(config));
    }

    public void UnpatchAll() => _harmony.UnpatchAll("com.claude.csharp-debug");
}
```

### 3.3 CSharpEvaluator.cs

```csharp
internal class CSharpEvaluator
{
    private Evaluator _evaluator;
    private bool _initialized;

    public void Initialize()
    {
        var settings = new CompilerSettings();
        var context = new CompilerContext(settings, new StreamReportPrinter(TextWriter.Null));
        _evaluator = new Evaluator(context);

        // 预导入常用命名空间
        _evaluator.Run("using UnityEngine;");
        _evaluator.Run("using System.Linq;");
        _evaluator.Run("using System.Collections.Generic;");
        _initialized = true;
    }

    public object Evaluate(string expression, object instance, object[] args)
    {
        try
        {
            // 注入上下文变量
            _evaluator.Run($"var __this = ({instance.GetType().FullName})__contextInstance;");
            return _evaluator.Evaluate(expression.Replace("this.", "__this."));
        }
        catch (Exception e)
        {
            return $"<eval-error: {e.Message}>";
        }
    }

    public bool EvaluateCondition(string condition, object instance, object[] args)
    {
        var result = Evaluate(condition, instance, args);
        return result is bool b && b;
    }
}
```

### 3.4 CSharpDebugMcpTool.cs

```csharp
[McpForUnityTool("csharp_debug",
    Description = "C# runtime debugging: method watch + expression evaluation",
    Group = "core")]
public static class CSharpDebugMcpTool
{
    public static object HandleCommand(JObject @params)
    {
        string action = @params["action"]?.ToString();

        switch (action)
        {
            case "watchMethod":
                return HandleWatchMethod(@params);
            case "watchMethodEx":
                return HandleWatchMethodEx(@params);
            case "eval":
                return HandleEval(@params);
            case "stop":
                CSharpDebugHook.Stop();
                return new SuccessResponse("All watches stopped.");
            default:
                return new ErrorResponse($"Unknown action: {action}");
        }
    }
}
```

## 4. 与 Lua 版 lua_bp_debug 的对比表

| 维度 | Lua lua_bp_debug | C# Hybrid (本方案) |
|------|-------------|---------------------|
| **断点粒度** | 行级 (debug.sethook "l") | 方法级 (Harmony Prefix/Postfix) |
| **局部变量** | ✅ debug.getlocal | ❌ 无法直接访问（需 Transpiler） |
| **参数** | ✅ debug.getlocal(1..n) | ✅ Harmony __args |
| **实例字段** | ✅ self.xxx | ✅ __instance 反射/Expression |
| **返回值** | ❌ 不直接支持 | ✅ Harmony __result |
| **表达式求值** | ❌ | ✅ Mono.CSharp.Evaluator |
| **条件断点** | ✅ 结构化 {var,op,val} | ✅ 完整 C# 表达式 |
| **注入方式** | MCP → LuaManager.DoString | MCP → CSharpDebugHook API |
| **注入延迟** | 即时（~1ms） | 即时（Harmony patch ~10ms） |
| **性能开销** | 中（line hook 每行触发） | 低（仅方法入口/出口触发） |
| **重启要求** | 不需要 | 不需要 |
| **状态影响** | 无 | 无 |
| **调用栈** | ✅ debug.getinfo | ✅ System.Diagnostics.StackTrace |
| **异常捕获** | ❌ | ✅ Harmony Finalizer |
| **协程支持** | ✅ patch coroutine | N/A (C# async 自动支持) |

## 5. 工作流设计 (Phase 0-6)

### Phase 0: 环境预检

```
环境预检：
  [?] HarmonyLib — 检查 typeof(HarmonyLib.Harmony) 是否存在
  [?] Mono.CSharp — 检查 typeof(Mono.CSharp.Evaluator) 是否存在
  [?] DebugCaptureWriter — Glob 检查文件存在
  [?] Unity Play Mode — MCP editor/state isPlaying
  [?] csharp_debug MCP — 测试调用
  [?] JSONL 已清理 — 截断文件
```

### Phase 1: 理解 Bug
同 Lua 版，收集 bug 描述/堆栈/截图。

### Phase 2: 代码分析
用 Grep/Read 定位相关 **C# 源文件**，标记可疑方法。
- 如果纯代码分析就能定位 → 直接修复
- 如果需要运行时数据 → Phase 3

### Phase 3: 生成断点命令

**基础监听**（Layer 1 only）:
```json
{
    "action": "watchMethod",
    "type": "RPGGame.Battle.BattleUnit",
    "method": "TakeDamage",
    "vars": ["damage", "this.hp", "this.shield"],
    "maxCaptures": 5
}
```

**高级监听**（Layer 1 + 2）:
```json
{
    "action": "watchMethodEx",
    "type": "RPGGame.Battle.BattleUnit",
    "method": "TakeDamage",
    "vars": ["damage", "this.hp"],
    "condition": "damage > this.shield",
    "eval": [
        "this.buffs.Count(b => b.IsActive)",
        "this.GetEffectiveDefense()",
        "this.hp - Mathf.Max(0, damage - this.shield)"
    ],
    "maxCaptures": 3
}
```

**即时求值**（仅 Layer 2，不设断点）:
```json
{
    "action": "eval",
    "expression": "FindObjectOfType<BattleManager>().currentTurn"
}
```

### Phase 4: 通过 MCP 注入

```
mcp__coplay-mcp__execute_custom_tool(
    tool_name="csharp_debug",
    parameters={上述 JSON}
)
```

注入后提示：
```
断点已注入：
  [1] BattleUnit.TakeDamage — Prefix+Postfix
      采集: damage, this.hp, this.shield
      条件: damage > this.shield
      表达式: 3 个
  max 3 captures

请在游戏中发起一次攻击（建议使用高伤害武器），命中后自动采集。
```

### Phase 5: 读取 JSONL

启动后台轮询 `Temp/cs_debug_capture.jsonl`，有数据时自动读取。

### Phase 6: 分析与迭代

解析 JSONL，检查变量值是否符合预期。
- 定位到根因 → 输出修复方案
- 需要更多数据 → 回到 Phase 3（调整目标方法 / 增加表达式）
- 3 轮未定位 → 输出阶段性报告

## 6. JSONL 数据格式

```jsonl
{"index":1,"type":"RPGGame.Battle.BattleUnit","method":"TakeDamage","phase":"prefix","params":{"damage":150},"instance":{"hp":1000,"shield":200,"level":5},"eval":{"this.buffs.Count(b=>b.IsActive)":2,"this.GetEffectiveDefense()":250,"this.hp-Mathf.Max(0,damage-this.shield)":1000},"stack":["BattleManager.ProcessAttack:45","TurnManager.Execute:120","GameLoop.Update:33"],"timestamp":1714000000}
{"index":2,"type":"RPGGame.Battle.BattleUnit","method":"TakeDamage","phase":"postfix","params":{"damage":150},"instance":{"hp":1000,"shield":200},"result":null,"timestamp":1714000001}
```

## 7. 可行性分析

### 80/20 分析

**覆盖的场景（~80%）**:
- 方法参数值异常 → Layer 1 直接看到
- 实例字段状态错误 → Layer 1 采集 this
- 返回值异常 → Layer 1 Postfix
- 复杂条件下的 bug → Layer 2 条件断点
- 深层对象状态 → Layer 2 表达式求值
- 异常吞没 → Layer 1 Finalizer
- 调用链追踪 → Layer 1 StackTrace

**未覆盖的场景（~20%）**:
- 方法内局部变量的中间状态（行级调试）
- 多线程竞态条件（需要精确时序控制）
- JIT 内联优化后的方法
- 属性 getter/setter 的隐式调用

### 优势

1. **务实高效**: 80% 场景覆盖，开发成本最低
2. **零重启热插拔**: Harmony patch 即时生效
3. **表达式求值强大**: 完整 C# 表达式，不仅是简单变量读取
4. **对标 Lua 版体验**: MCP 注入 → 用户触发 → 自动采集 → Claude 分析
5. **可渐进增强**: 先做 Layer 1，后加 Layer 2

### 劣势

1. **方法级粒度**: 不能在方法内任意行设断点
2. **无局部变量**: 只能看到参数和字段，看不到方法内临时变量
3. **Mono.CSharp 限制**: 不支持所有 C# 新语法（async/await、pattern matching）
4. **需要引入 HarmonyLib**: 约 200KB 第三方库

### 风险

- Mono.CSharp.Evaluator 在 Unity 2022 Mono 版本中的兼容性（需要验证）
- Harmony 与其他运行时 patch 工具的兼容性

## 8. 实现复杂度评估

| 维度 | 评估 |
|------|------|
| **Phase 1: 仅 Layer 1** | 3-5 天（Harmony + JSONL + MCP） |
| **Phase 2: 加 Layer 2** | +3-5 天（Mono.CSharp 集成） |
| **总计** | 6-10 天 |
| **外部依赖** | HarmonyLib 2.x（MIT） |
| **维护成本** | 低（两个组件都很稳定） |
| **推荐度** | ⭐⭐⭐⭐⭐ |

## 9. 完整调试会话模拟

```
=== 调试会话：英雄受到攻击后 HP 没有正确扣减 ===

Claude: 读取 BattleUnit.cs...
        TakeDamage 方法在 line 115-130。
        可疑：shield 计算可能有问题。
        设置断点...

→ MCP: {"action":"watchMethodEx","type":"BattleUnit","method":"TakeDamage",
        "vars":["damage","this.hp","this.shield","this.def"],
        "condition":"damage > 0",
        "eval":["this.GetEffectiveDefense()","Mathf.Max(0,damage-this.shield)"],
        "maxCaptures":3}

Claude: 断点已注入 BattleUnit.TakeDamage (Prefix+Postfix)
        请在游戏中攻击任意敌人。

--- 用户操作 ---

Claude: [读取 JSONL] 采集到 2 条记录:

  Capture #1 (prefix):
    damage = 150, this.hp = 1000, this.shield = 200, this.def = 50
    eval: GetEffectiveDefense() = 250, Mathf.Max(0, 150-200) = 0

  Capture #2 (postfix):
    this.hp = 1000 (未变!), result = false

  分析: damage(150) < shield(200)，所以 Mathf.Max(0, 150-200) = 0。
        HP 没有扣减是正确行为——shield 完全吸收了伤害。
        但 shield 应该被消耗。检查 shield 扣减逻辑...

→ MCP: {"action":"watchMethodEx","type":"BattleUnit","method":"ConsumeShield",
        "vars":["amount","this.shield"],
        "eval":["this.shield - amount"]}

Claude: 未捕获到 ConsumeShield 调用！
        根因: TakeDamage 中缺少调用 ConsumeShield。
        修复方案: 在 line 125 后添加 ConsumeShield(Mathf.Min(damage, this.shield))

→ 修复代码 → 验证
```

## 10. 实施路线图

```
Week 1: Layer 1 基础版
  - HarmonyPatchEngine 核心
  - CSharpDebugHook API (WatchMethod/Stop)
  - CSharpDebugMcpTool MCP 接口
  - JSONL 输出（复用 DebugCaptureWriter）
  - 冒烟测试

Week 2: Layer 2 + Skill 整合
  - Mono.CSharp.Evaluator 集成
  - WatchMethodEx (条件断点 + 表达式)
  - Eval 立即求值
  - SKILL.md 编写（Phase 0-6 工作流）
  - 端到端测试

可选 Week 3: 增强
  - Transpiler 行级断点（高阶需求）
  - TypeResolver 智能模糊匹配
  - 多线程安全加固
```
