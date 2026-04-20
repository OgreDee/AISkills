# AISkills

Claude Code 技能（Skills）集合，为 AI 辅助开发提供专项能力扩展。

## Skills 列表

| Skill | 说明 |
|-------|------|
| [bp_debug](./bp_debug/) | 自动断点调试：分析 Bug → 设断点 → 注入运行时 → 读取 JSONL → 分析变量 → 迭代定位 Lua 根因，全流程自动化 |

## 使用方式

每个 Skill 目录包含：

- `SKILL.md` — 技能 Prompt（部署到 `.claude/skills/` 后由 Claude 自动加载）
- `install.sh` — 一键部署脚本
- `scripts/` — 依赖的运行时脚本

将 Skill 目录复制到目标项目的 `.claude/skills/` 下即可启用。

## 许可

内部工具，仅供项目内部使用。
