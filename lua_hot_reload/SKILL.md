---
name: lua_hot_reload
description: 分析修改的 Lua 文件是否可热重载，可以则通过 MCP 执行，不可以则提示原因
tags: [lua, unity]
---

# Lua 热重载

修改 Lua 文件后，自动判定是否可安全热重载。可以则通过 Unity MCP 执行，不可以则给出原因。

## 安装

首次使用前需部署运行时文件到项目中：

```bash
bash .claude/skills/lua_hot_reload/install.sh
```

部署的文件：
- `Assets/HotRes/Lua/Utils/HotReload.txt` — Lua 热重载核心（in-place patch）
- `Assets/Editor/Claude/LuaHotReloadMcpTool.cs` — MCP 自定义工具

## 使用方法

```bash
# 指定文件路径（相对于 Assets/HotRes/Lua/，不含 .txt）
/lua_hot_reload RPG/Entity/State/RPGHeroIdle

# 无参数：自动检测本次会话最近修改的 Lua 文件
/lua_hot_reload
```

## 执行流程

### Step 1: 确定目标文件

- 有参数：直接使用，补全为 `Assets/HotRes/Lua/{arg}.txt`
- 无参数：执行 `git diff --name-only HEAD` 找 `Assets/HotRes/Lua/` 下的 `.txt` 文件
  - 多个文件时列出让用户选择
  - 无修改文件时提示退出

**路径变量**（后续步骤引用）：
- `FILE_PATH`: 完整路径，如 `Assets/HotRes/Lua/RPG/Entity/State/RPGHeroIdle.txt`
- `MOD_PATH`: require 路径，如 `RPG/Entity/State/RPGHeroIdle`

### Step 2: 确认已安装

检查 `Assets/HotRes/Lua/Utils/HotReload.txt` 是否存在：
- 不存在 → 自动执行 `bash .claude/skills/lua_hot_reload/install.sh` 安装
- 存在 → 继续

### Step 3: 逐条检查判定规则

按优先级顺序检查，**任一命中立即判定失败并停止**：

#### 规则 1: Global.txt 注册检查

用 Grep 搜索 `Assets/HotRes/Lua/Global.txt` 中是否包含 `MOD_PATH`。

- 命中 → ❌ 失败，原因：`该文件在 Global.txt 中注册为全局类`

#### 规则 2: 全局类定义检查

用 Read 读取目标文件，检查是否满足以下模式：
- 文件中存在 `ClassName = class(` 格式（大驼峰变量名 = class）
- 且文件中**不存在** `local class = class(` 或 `local class = {}`

- 命中 → ❌ 失败，原因：`该文件使用全局类定义模式（{ClassName} = class），已有实例不会更新`

#### 规则 3: 被其他文件依赖

用 Grep 搜索整个 `Assets/HotRes/Lua/` 目录中 `require` 了 `MOD_PATH` 的文件（排除自身和 Global.txt）。

搜索 pattern: `require.*MOD_PATH`（对路径中的 `/` 同时匹配 `/` 和路径分隔变体）

- 有结果 → ❌ 失败，原因：`该文件被 N 个其他文件依赖: {文件列表}`

#### 规则 4: ctor 变更检查

用 Bash 执行 `git diff HEAD -- FILE_PATH`，检查 diff 输出中是否包含 `function class:ctor` 或 `function ClassName:ctor` 的变更行（以 `+` 或 `-` 开头）。

- 命中 → ❌ 失败，原因：`本次修改涉及 ctor 函数，已有实例不会重新构造`

#### 全部通过 → ✅ 可热重载

### Step 4: 执行或提示

**✅ 可热重载**：

通过 MCP 自有工具执行：
```
mcp__coplay-mcp__execute_custom_tool(
  tool_name="lua_hot_reload",
  parameters={"modPath": "MOD_PATH"}
)
```

输出：
```
✅ 热重载成功: {MOD_PATH}
   已通过 HotReload.Execute() 原地 patch 模块
```

如果 MCP 调用失败（Unity 未运行/未连接），输出手动方案：
```
⚠️ MCP 不可用，请在 Unity Console 中手动执行：
   local HR = require('Utils/HotReload'); HR.Execute("MOD_PATH")
```

**❌ 不可热重载**：

输出：
```
❌ 无法热重载: {MOD_PATH}
   原因: {具体原因}
   建议: 请重启 Play Mode 使修改生效
```

## 热重载原理

自实现的 `HotReload.Execute(modPath)` 逻辑：
1. 取出 `package.loaded[modPath]` 的旧 module table
2. 清除缓存 `package.loaded[modPath] = nil`
3. 重新 `require(modPath)` 得到新 module table
4. 将新 table 中的 function/field **patch 回旧 table**（保持引用不变）
5. 恢复 `package.loaded[modPath]` 指向旧 table

优势：已有实例的 `__index` 指向旧 class table，patch 后自动获得新方法，无需重新打开面板。

## 约束

- 仅 Editor Play Mode 下有效
- 不做级联重载：有依赖方直接判定失败
- 不处理 ctor 变更：已有实例的字段不会重新初始化
- patch 仅覆盖 function 类型字段，data 字段原值保留
