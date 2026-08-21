# GitHub 发布就绪报告

日期：2026-08-21
项目版本：0.9.12
状态：**技术源码包、许可证边界和角色人工视觉复核已完成；允许首次公开源码。**

## 已完成

- 公开源码 allowlist：154 个文件，约 11.46 MB；
- `dotnet build VoiceMemoryDemo.sln -c Release`：0 警告、0 错误；
- 11 个可公开 smoke/benchmark 工程：全部编译通过；
- 4 个无云费用离线功能测试：
  - 中英文 UI/12 种英文悬浮状态及输出语言标识：PASS；
  - 纠错词典增删改查：PASS；
  - 会议报告解析、预览与删除边界：PASS；
  - 中英文会议模板本地校验：PASS；
- NuGet 已知漏洞与弃用依赖检查：PASS；
- Markdown 本地链接：PASS；
- 高置信度密钥、私钥、本机用户路径、手机号扫描：PASS；
- GitHub 源码包生成、逐文件 manifest、SHA-256、解压复核和解压后干净构建：PASS；
- 本地旧 Windows QA ZIP 仍明确标记为不得上传。

详细边界和未执行项见 [PRE_UPLOAD_AUDIT.zh-CN.md](PRE_UPLOAD_AUDIT.zh-CN.md)。

## 已完成的许可证决定

### 1. 项目许可证与主体

项目所有者已经确认：

- `Copyright 2026 Xiang Zhang`；
- 源代码采用 Apache-2.0，允许第三方商业使用、修改和再分发代码；
- 小夜和 UI 美术允许个人及其他非商业下载、运行、学习和非商业 Fork；
- 商业发行必须替换小夜相关素材。

准确宣传是“源代码采用 Apache-2.0，品牌美术采用独立非商业许可证”。

## 角色素材复核结论

公开 allowlist 不含用户提供的参考截图或候选图。最终“小夜”素材未发现第三方名称、徽标、商标或与单一已知角色一一对应的标志性组合；项目所有者已接受残余风险并按独立非商业素材许可证发布。该结论是发布前人工视觉复核，不是法律意见。

## 二进制 Release 边界：Lite 可发布，含模型完整包仍阻塞

从公开源码包干净构建的 Lite Windows ZIP 不含本地 ONNX 模型权重，可以发布。包含 E5、Silero VAD、CAMPPlus 与 pyannote segmentation 权重的完整 Windows ZIP 仍须在上传前逐项保存：

- 上游地址和固定 revision；
- 权重本身的许可证/再分发证明，而不只是代码仓库许可证；
- 原始文件与本地转换文件的 SHA-256；
- 必须随包附带的 NOTICE/LICENSE。

`Build-WindowsRelease.ps1` 因此默认拒绝含模型权重的正式打包，只有在人工完成上述复核后才能显式传入 `-AcknowledgeModelLicenseReview`。

## P1：首次公开当天完成

- 在 GitHub 创建空仓库后先运行 `Test-PublicRelease.ps1`，再人工检查首次待提交清单；
- 使用 Gitleaks/TruffleHog 对真实 Git 索引再扫描一次；
- 推送后观察 Windows CI 的“无模型权重干净构建”；
- 开启 Private vulnerability reporting、Dependabot 与 branch protection；
- Release notes 明确 BYOK、第三方计费、会议同意、AI 总结需人工复核和 SmartScreen；
- 后续考虑 Windows 代码签名、自动更新与后端短期凭证。
- 修复当前网络环境下的 Google Maven TLS/依赖解析后，重新完成 Android 全量构建再发布 APK。

## 明确排除

- `Models` 权重与压缩包；
- `test-data`、`test-results`、`artifacts`；
- 真实会议音频、字幕、报告、绝对本机路径和评测原始响应；
- `publish`、`tools`、所有 `bin/obj/build`；
- `Assets/CharacterCandidates` 与 QA 截图；
- 内部盲测/密钥持有/评测协议代码和结果。

## 未执行的外部动作

- 没有 `git init`；
- 没有 `git add`、commit 或 push；
- 没有创建 GitHub 远程仓库；
- 没有创建 Release 或上传附件。
