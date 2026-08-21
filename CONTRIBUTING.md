# Contributing

感谢参与速说速记X。当前项目优先解决可靠性、隐私和可复现性，不接受为了“看起来更智能”而扩大云端数据范围的改动。

## 开始之前

1. 先搜索现有 Issue；
2. 大功能先开 Issue 说明场景、隐私影响、供应商费用和失败回退；
3. 不要提交真实用户录音、会议报告、密钥、账单截图或本地数据库；
4. 不要把模型权重、SDK、工具链或发布包放进 Git。

## 本地验证

```powershell
dotnet restore .\VoiceMemoryDemo.sln
dotnet build .\VoiceMemoryDemo.sln -c Release --no-restore
.\scripts\Test-PublicRelease.ps1 -SkipBuild
```

与功能相关的 smoke test 应单独运行并说明所需外部服务。没有云端凭证的测试必须保持离线。

## Pull Request 要求

- 说明改了什么、为什么改；
- 写明是否新增云请求、数据保存、权限、模型或第三方费用；
- UI 改动附脱敏截图；
- 热键、剪贴板、ASR、会议报告等路径给出复现步骤；
- 新依赖必须更新 `THIRD_PARTY_NOTICES.md`；
- 不降低无语音保护、密钥保护、事实/数字检查和禁止虚构行动项等安全约束。

## 代码风格

- C# 启用 nullable，异步 I/O 传递 `CancellationToken`；
- 失败路径必须可诊断，但日志不得包含密钥、音频或剪贴板正文；
- 网络/模型失败应明确回退，不得把回退结果伪装为云端成功；
- 新 UI 文本同时补齐中文和英文。
