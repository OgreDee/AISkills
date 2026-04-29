---
name: bp
description: 统一断点调试入口：自动判定 Lua/C# 层并路由到 lua_bp_debug 或 cs_bp_debug
tags: [debug, lua, csharp, unity]
---

# 断点调试（统一入口）

根据 bug 信息自动判定属于 Lua 层还是 C# 层，然后路由到对应的专项调试 skill。

## 使用方法

```bash
# 描述 bug 即可，自动判定语言层
/bp 英雄受击后血量没有扣减

# 也可以明确指定
/bp lua 战斗中技能释放崩溃
/bp cs BattleUnit.TakeDamage 返回值异常
```

## 执行流程

### Step 1: 判定语言层

**有明确指定**：用户参数以 `lua` 或 `cs`/`csharp`/`c#` 开头 → 直接使用指定层。

**无明确指定**：从 bug 描述和上下文中推断：

| 信号 | 判定 |
|------|------|
| 堆栈包含 `.cs` 文件路径、`Type.Method` 格式 | → C# |
| 堆栈包含 `@path` 或 Lua source 路径、`.txt` 文件 | → Lua |
| 提到 C# 类名（含命名空间，如 `RPGBattleModule.BattleUnit`） | → C# |
| 提到 Lua 路径（如 `RPG/Entity/EntityHero`）或 require 路径 | → Lua |
| 涉及 UI 面板、Lua 业务逻辑、配置表读取 | → Lua（大概率） |
| 涉及引擎底层、渲染、物理、序列化、C# 框架层 | → C# |
| 无法判定 | → 询问用户 |

### Step 2: 路由调用

判定完成后，调用对应的 skill：

- **Lua 层** → 调用 `/lua_bp_debug`（Skill 工具）
- **C# 层** → 调用 `/cs_bp_debug`（Skill 工具）

将用户原始的 bug 描述原样传递给目标 skill，由目标 skill 从 Phase 0 开始执行完整流程。

### Step 3: 跨层切换

调试过程中如果发现 bug 根因在另一层（例如 Lua 调用的 C# 方法有问题），提示用户并建议切换：

```
当前在 Lua 层调试，但根因疑似在 C# 层（BattleUnit.TakeDamage 返回值异常）。
是否切换到 C# 断点调试？
```

用户同意后调用另一层的 skill 继续调试。
