# GitHub 首次发布检查表

## 必须在上传前完成

- [x] 项目所有者确认代码使用 Apache-2.0；版权所有者为 Xiang Zhang。
- [x] “小夜”美术采用个人及其他非商业使用许可；商业发行必须替换素材。
- [x] 确认公开包不含参考截图/候选图，并对最终“小夜”素材完成人工视觉、商标与明显相似性复核；保留非法律意见说明。
- [ ] 确认 Git 中没有 `Models` 权重、`test-data`、`test-results`、`artifacts`、`publish`、`tools` 和 `CharacterCandidates`。
- [ ] 首次 `git add` 前运行 `.\scripts\Test-PublicRelease.ps1 -SkipBuild -ListFiles`，只按该清单选择文件。
- [ ] 运行 `.\scripts\Test-PublicRelease.ps1` 并通过。
- [ ] 在一个不含本地模型和缓存的干净目录中重新克隆并构建。
- [ ] 运行 `.\scripts\Build-GitHubSourcePackage.ps1`，核对 ZIP、manifest 和 SHA-256。
- [ ] 按 [docs/GITHUB_UPLOAD_GUIDE.zh-CN.md](docs/GITHUB_UPLOAD_GUIDE.zh-CN.md) 从解压目录上传，不把 ZIP 本身提交为仓库内容。
- [ ] 用新建的测试凭证完成一次右 Alt 和左 Alt 验证，然后撤销该测试凭证。
- [ ] 检查中文/英文 README、隐私说明和当前版本号。
- [ ] 开启 GitHub Private vulnerability reporting。
- [ ] 确认仓库没有真实会议录音、报告、个人路径或账户截图。

## 发布附件

- [x] 从公开源码包干净构建无模型权重的 Windows Lite ZIP，并生成 `.sha256`。
- [ ] 逐项确认模型再分发许可后，运行 `.\scripts\Build-WindowsRelease.ps1 -AcknowledgeModelLicenseReview`。
- [ ] Release 仅附 ZIP/APK 和 `.sha256`，不提交进源码 Git。
- [ ] 若发布可选模型包，逐个核验许可证、来源 revision 与 SHA-256。
- [ ] 如 Windows 程序尚未签名，在 Release notes 明确 SmartScreen 风险。

## 发布边界

- 首次只提交公开 allowlist 中的源码；
- 未经模型权重再分发许可复核，不上传包含模型权重的 Release 附件；
- 不宣传本地说话人路由已经节费；
- 不把实验性的 DeepSeek 上下文专名纠错默认开启；
- 不把 Demo 描述为生产级托管服务。
