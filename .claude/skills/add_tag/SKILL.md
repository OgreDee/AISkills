---
name: add_tag
description: 为目标 Skill 添加 Tag 标签，并同步更新根目录 README 索引
---

# 添加标签

为指定 Skill 添加 Tag 标签，标签存储在 SKILL.md 的 frontmatter 中，同时同步更新根目录 README 的 Skills 列表。

## 使用方法

```bash
# 为指定 skill 添加标签
/add_tag bp_debug lua,debugging,mcp

# 交互模式：无参数时列出所有 skill 让用户选择
/add_tag
```

## 参数格式

```
/add_tag [SKILL_NAME] [TAG1,TAG2,...]
```

- `SKILL_NAME`（可选）：目标 skill 目录名
- `TAGS`（可选）：逗号分隔的标签列表，不含空格

## 执行流程

### Step 1: 确定目标 Skill

- **有第一个参数**：使用该名称作为目标 skill
- **无参数**：扫描项目根目录下所有含 `SKILL.md` 的子目录，列出让用户选择

验证目标 skill 目录存在且包含 `SKILL.md`，否则报错退出。

**变量**：
- `SKILL_NAME`: skill 目录名，如 `bp_debug`
- `SKILL_DIR`: skill 完整路径，如 `./bp_debug/`

### Step 2: 确定要添加的 Tags

- **有第二个参数**：解析逗号分隔的标签列表
- **无参数**：询问用户要添加哪些标签，可提供建议

**标签规范**：
- 全小写，英文
- 单词间用 `-` 连接（如 `hot-reload`）
- 不超过 20 字符
- 常见标签参考：`lua`, `debugging`, `mcp`, `unity`, `hot-reload`, `automation`, `analysis`, `safety-check`, `runtime`, `editor-tool`

### Step 3: 读取现有 Tags

读取 `{SKILL_DIR}/SKILL.md` 的 frontmatter，检查是否已有 `tags` 字段：

```yaml
---
name: bp_debug
description: ...
tags: [lua, debugging]
---
```

- 如果已有 `tags`：合并新标签（去重，保持字母序）
- 如果没有 `tags`：新增该字段

### Step 4: 更新 SKILL.md Frontmatter

在 SKILL.md 的 YAML frontmatter 中更新 `tags` 字段。

**规则**：
- `tags` 字段放在 `description` 之后
- 使用 YAML flow 格式：`tags: [tag1, tag2, tag3]`
- 标签按字母序排列
- 仅修改 frontmatter 部分，不动正文内容

**更新前**：
```yaml
---
name: bp_debug
description: 自动断点调试...
---
```

**更新后**：
```yaml
---
name: bp_debug
description: 自动断点调试...
tags: [debugging, lua, mcp]
---
```

### Step 5: 更新根 README

编辑项目根目录的 `README.md`，在 Skills 列表表格中为对应 skill 添加标签显示。

**表格格式**：

```markdown
| Skill | Tags | 说明 |
|-------|------|------|
| [bp_debug](./bp_debug/) | `lua` `debugging` `mcp` | 自动断点调试：... |
| [lua_hot_reload](./lua_hot_reload/) | `lua` `hot-reload` `mcp` | 判定 Lua 文件... |
```

**规则**：
- 如果表格当前只有 2 列（Skill + 说明），需插入 Tags 列变为 3 列
- 如果表格已有 Tags 列，仅更新对应行的标签
- 标签使用行内代码格式（`` `tag` ``），多标签间用空格分隔
- 标签按字母序排列
- 不要修改其他行的说明内容

**迁移旧格式**：如果当前表格是 2 列格式：
```markdown
| Skill | 说明 |
|-------|------|
```

需要改为 3 列格式：
```markdown
| Skill | Tags | 说明 |
|-------|------|------|
```

对于已有的 skill 行，如果其 SKILL.md 中已有 `tags` 字段则读取填入，否则 Tags 列留空。

### Step 6: 确认输出

完成后向用户报告：
1. 已更新：`{SKILL_DIR}/SKILL.md`（显示新的 tags 字段）
2. 已更新：根 `README.md`（显示变更后的表格行）
3. 列出最终完整标签列表

## 注意事项

- 如果用户指定的标签已存在，跳过并提示"标签已存在"
- 删除标签不是本 skill 的职责，如用户要求删除标签，直接手动编辑 frontmatter 和 README 即可
- 保持 SKILL.md 中除 frontmatter 外的所有内容不变
- 如果根 README 中找不到对应 skill 的行，先提醒用户执行 `/add_readme`
