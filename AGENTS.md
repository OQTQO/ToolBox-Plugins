# ToolBox 插件仓库代理规则

开始工作前：

1. 若存在 `..\WORKSPACE.md`，按其中路由确认仓库范围。
2. 运行 `powershell -ExecutionPolicy Bypass -File .\tools\Get-ProjectContext.ps1` 读取任务摘要、软件基线、SDK 版本、HEAD 与未提交修改。
3. 再按需读取 `AI.md`、`docs/compatibility.md`、目标插件 README、Manifest、项目和测试。
4. 严重中断恢复、架构审计或跨仓库协议升级时使用 `Get-ProjectContext.ps1 -Full`。

`..\软件` 是平台契约权威源。发生冲突时修改插件，不要求 Host/Core/Worker 保留旧插件行为。插件依赖已发布 SDK/DevKit，不直接引用软件源码。

完成任务时更新当前任务文件并执行与风险匹配的验证。未经用户明确要求，不提交、推送、发布或回滚已有修改。
