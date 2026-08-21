# GitHub 上传前整体排查

日期：2026-08-21
版本：0.9.14
结论：**技术源码包与许可证边界通过；项目所有者已授权发布源码。最终角色已完成人工视觉复核，可公开，但该复核不是法律意见。**

## 1. 已通过

| 检查 | 结果 |
|---|---|
| Windows Release 构建 | PASS，0 警告、0 错误 |
| 公开测试工程编译 | PASS，11/11 |
| 离线 UI 本地化 | PASS，中英文、12 种英文悬浮状态和输出语言标识 |
| 本地纠错词典 CRUD | PASS |
| 会议报告解析/渲染 | PASS |
| 中英文会议模板本地校验 | PASS |
| NuGet 已知漏洞 | PASS，未发现 |
| NuGet 已弃用依赖 | PASS，未发现 |
| 公开文件 allowlist | PASS，154 个文件，约 11.46 MB |
| 高置信度密钥/私钥/个人路径/手机号扫描 | PASS |
| Markdown 本地链接 | PASS |
| GitHub 源码 ZIP 逐文件哈希与解压复核 | PASS |
| 从解压后的源码包干净构建 | PASS，0 警告、0 错误 |

源码包只包含公开 allowlist：没有 ONNX 权重、真实音频、会议报告、内部评测、API 响应、本地数据库、日志、构建产物或密钥。

## 2. 当前不应对外宣称已通过

- 本轮没有重新调用腾讯 ASR 或 Speaker 2.0。为验证目标语言链路，执行了 4 条短文本 DeepSeek 冒烟用例，其中 1 条为中文转英文；其余发布检查均为本地离线测试。
- 没有重新运行依赖真实麦克风、系统回环或第三方软件输入框的硬件/桌面自动化测试。
- Android 源码仍包含在仓库中，但当前网络环境对 Google Maven 出现 TLS/依赖解析失败，因此本轮没有得到新的 Android 全量构建 PASS；这不影响 Windows 源码包，但 Android Demo 不应在本轮 Release notes 中宣称已重新验证。
- Windows 可执行文件尚未代码签名，旧本地 QA ZIP 不得当作正式 Release 附件。

## 3. 已完成的授权决定

### 代码许可证与版权所有者

项目所有者已经确认：

- `NOTICE` 使用 `Copyright 2026 Xiang Zhang`；
- 源代码采用 Apache-2.0，允许他人商业使用、修改和再分发代码。

### 小夜与 UI 美术

当前 [ASSET_LICENSE.md](../ASSET_LICENSE.md) 允许个人及其他非商业下载、运行、学习和非商业 Fork；商业发行必须替换小夜相关素材。

## 4. 角色素材人工复核

- 公开 allowlist 只包含最终“小夜”素材，不包含用户提供的角色参考截图或候选图；
- 最终素材未出现第三方名称、文字、徽标或可识别商标；
- 角色采用紫色短发、青色星形发饰、橙色围巾和深蓝科技服装等组合，未发现与单一已知角色一一对应的标志性组合；
- 项目所有者已确认按 [ASSET_LICENSE.md](../ASSET_LICENSE.md) 的非商业素材边界公开。

仍需注意：人工视觉检查不能证明训练或参考来源的完整权利链，也不构成法律意见。收到可信权利投诉时，应先下架争议素材并更换角色资源。

## 5. 二进制 Release 边界

源码仓库和 Lite Windows ZIP 均排除了模型权重，因此可以发布。包含 E5、Silero VAD、CAMPPlus、pyannote 等模型权重的完整 Windows ZIP 仍需逐文件确认权重许可证、来源 revision、SHA-256 和随包 NOTICE。未完成前：

- 只上传从公开源码包干净构建的 `VoiceMemo-X-0.9.14-win-x64-lite.zip`；
- 不上传任何包含未复核模型权重的完整包；
- 不上传旧 APK 作为正式版本；
- Release notes 明确 Lite 包的降级边界和 SmartScreen 风险。

## 6. 上传后必须再做

1. 在真实 Git 索引上再次运行 `scripts/Test-PublicRelease.ps1`。
2. 人工查看首次 commit 清单；CI 通过后再考虑 Public。
3. 在全新 clone 中构建一次。
4. 开启 Dependabot、Private vulnerability reporting 和 main 分支保护。
5. 用新建、可撤销的测试凭证做一次付费端到端验证，随后撤销凭证。

本报告只说明当前工作区和源码包的状态，不是安全认证、法律意见或第三方 API SLA。
