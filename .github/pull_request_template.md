## 改动

请简要说明改了什么。

## 为什么

请说明为什么需要这项改动。

## 验证

- [ ] `dotnet build .\VoiceMemoryDemo.sln -c Release`
- [ ] `.\scripts\Test-PublicRelease.ps1 -SkipBuild`
- [ ] 热键/剪贴板/会议/导入等受影响路径已手工验证
- [ ] UI 文本同时检查了中文与英文

## 数据、权限与费用

- [ ] 不新增云端数据或费用
- [ ] 如有新增，已在下方说明供应商、数据、保留和失败回退

说明：

## 发布检查

- [ ] 没有密钥、录音、私人报告、数据库、日志、模型权重或构建产物
- [ ] 新依赖已更新 `THIRD_PARTY_NOTICES.md`
- [ ] 截图和日志已脱敏
