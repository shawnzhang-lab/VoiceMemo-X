<p align="center">
  <img src="src/VoiceMemoryDemo.App/Assets/app-icon-xiaoye-v4.png" width="104" alt="速说速记X 图标">
</p>

<h1 align="center">速说速记X / VoiceMemo X</h1>

<p align="center">
  Windows 全局语音输入与实时会议纪要 Demo
</p>

<p align="center">
  <a href="README.en.md">English</a> ·
  <a href="docs/DOWNLOAD_GUIDE.zh-CN.md">下载</a> ·
  <a href="docs/WINDOWS_INSTALL_GUIDE.zh-CN.md">安装</a> ·
  <a href="docs/FIRST_USE_GUIDE.zh-CN.md">使用</a> ·
  <a href="docs/API_SETUP_INDEX.zh-CN.md">API 配置</a> ·
  <a href="docs/PRE_UPLOAD_AUDIT.zh-CN.md">发布审计</a> ·
  <a href="docs/PRODUCT_INTRO_DRAFTS.zh-CN.md">产品介绍</a> ·
  <a href="docs/DEMO_VIDEO_PLAN.zh-CN.md">演示计划</a> ·
  <a href="docs/ARCHITECTURE.zh-CN.md">架构</a> ·
  <a href="PRIVACY.md">隐私</a>
</p>

> [!IMPORTANT]
> 这是一个个人自带密钥（BYOK）的实验性项目，不是腾讯云、DeepSeek、飞书或 OpenAI 的官方产品。使用云端 ASR 和大模型会产生第三方费用；录制会议前请取得所有参与者同意。

## 它能做什么

### 右 Alt：日常语音输入

1. 把光标放入聊天、浏览器、文档或搜索框。
2. 按一次 **右 Alt** 开始；5 秒内没有检测到语音会自动关闭，不连接云端。
3. 再按一次 **右 Alt** 结束。
4. 腾讯云实时 ASR 返回文字；可选 DeepSeek 整理、翻译和本地词典纠错；结果自动输入到原输入框。

### 左 Alt：实时会议纪要

1. 按一次 **左 Alt**，同时采集默认麦克风与 Windows 默认播放设备的声音。
2. 再按一次 **左 Alt** 结束。
3. 腾讯 Speaker 2.0 返回匿名说话人标签，DeepSeek 生成 Markdown 会议报告。
4. 在“会议报告”页查看进度、复制、导出或删除报告。

还可以导入 MP3、WAV、M4A、AAC、WMA、MP4、MOV 或 M4V。导入任务固定使用 Speaker 2.0；本地说话人数路由器仍处于影子验证期，不会为了节费切换到普通 ASR。

## 主要能力

- 全局左右 Alt 热键，以及区分日常输入/会议模式的悬浮状态窗。
- 中文、英语、粤语自动识别；可把中文整理为英语、日语等目标语言。
- 去口头禅、修正明显识别错误、智能分点和可编辑提示模板。
- SQLite 本地记忆、专有词纠错词典、可选语义向量检索。
- 麦克风 + WASAPI Loopback 会议收音、匿名说话人分离和报告历史。
- 中文/英文客户端与悬浮窗。
- Windows DPAPI 加密保存本机密钥；Android Demo 使用 Android Keystore。

## 快速开始

普通用户可在 [Releases](https://github.com/shawnzhang-lab/VoiceMemo-X/releases) 下载 `VoiceMemo-X-0.9.14-win-x64-lite.zip` 和同名 `.sha256` 文件。Lite 包不含本地 ONNX 模型权重，但日常云端语音输入、翻译和会议报告仍可使用；本地语义向量与本地说话人数预判会降级。

源码运行环境：Windows 10/11 x64，.NET 8 SDK；云端功能需要腾讯云 ASR 和 DeepSeek API 账户。

```powershell
git clone https://github.com/shawnzhang-lab/VoiceMemo-X.git
cd VoiceMemo-X
.\run-demo.ps1
```

首次启动后，在“连接设置”填写：

- 腾讯云 AppID、SecretID、SecretKey；
- DeepSeek API Key；
- 按需修改 ASR 与模型设置。

完整步骤见[下载教程](docs/DOWNLOAD_GUIDE.zh-CN.md)、[Windows 安装教程](docs/WINDOWS_INSTALL_GUIDE.zh-CN.md)、[源码运行说明](docs/INSTALLATION.zh-CN.md)和 [API 配置索引](docs/API_SETUP_INDEX.zh-CN.md)。模型权重不存放在 Git 仓库中；没有本地模型时，语义向量与本地说话人数预判会降级，但普通语音输入仍可运行。

## 本地构建

```powershell
dotnet restore .\VoiceMemoryDemo.sln
dotnet build .\VoiceMemoryDemo.sln -c Release --no-restore
.\publish-win-x64.ps1
```

发布目录为 `publish\速说速记X-win-x64`。不要把 `publish/` 提交进 Git。无模型权重的 Lite 包可以作为 GitHub Release 附件并附 SHA-256；包含任何第三方模型权重的完整包仍须先完成逐文件再分发许可复核。

准备 GitHub 源码上传包：

```powershell
.\scripts\Build-GitHubSourcePackage.ps1
```

该脚本只复制公开 allowlist 中的文件，并生成 ZIP、逐文件 manifest 和 SHA-256；不会运行 `git init`、commit 或上传。项目所有者的上传步骤见 [GitHub 上传教程](docs/GITHUB_UPLOAD_GUIDE.zh-CN.md)。

Android Demo 的构建与限制见 [android/VoiceMemoryDemo.Android/README.md](android/VoiceMemoryDemo.Android/README.md)。它不提供 iOS 的系统级全局输入体验。

## 数据与隐私

默认本地数据目录：

```text
%LOCALAPPDATA%\VoiceMemoryDemo\settings.dat
%LOCALAPPDATA%\VoiceMemoryDemo\voice-memory.db
%LOCALAPPDATA%\VoiceMemoryDemo\Meetings\
%LOCALAPPDATA%\VoiceMemoryDemo\diagnostics.log
%LOCALAPPDATA%\VoiceMemoryDemo\speaker-router.jsonl
```

- 音频发送给腾讯云 ASR；
- 开启 AI 整理时，转写文字和少量命中记忆发送给 DeepSeek；
- 密钥只在当前 Windows 用户下使用 DPAPI 加密；
- 诊断日志不记录剪贴板文字，但会记录剪贴板占用进程信息；
- 删除应用不会自动删除上述目录。

详情见 [PRIVACY.md](PRIVACY.md)。不要在公开发行版里内置腾讯云主账号 SecretKey；面向不受信用户的产品应改用后端代理或短期凭证。

## 当前限制

- Windows 客户端使用默认麦克风/播放设备，尚未提供完整设备选择器。
- 目标程序以管理员身份运行时，普通权限客户端可能只能复制到剪贴板，无法自动注入。
- Speaker 2.0 会产生独立用量；当前本地路由器只做影子验证，不应宣传为已节费。
- 会议报告是 AI 生成内容，可能漏掉、误解或编造信息；关键决定必须人工复核。
- Windows 发布包尚未代码签名，下载时可能触发 SmartScreen。
- Android 版本是功能验证 Demo，无法像 Windows 一样向所有第三方输入框直接注入文字。

## 开源边界

- 源代码：Copyright 2026 Xiang Zhang，采用 Apache-2.0。
- “小夜”角色和 UI 插画：允许个人非商业下载、运行、学习和非商业 Fork，但禁止商用；商业发行必须替换相关素材，详见 [ASSET_LICENSE.md](ASSET_LICENSE.md)。
- 大模型权重、真实测试音频、内部评测结果、构建产物和候选角色图不会进入仓库。
- 第三方组件与模型来源见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

这意味着当前准备包是“代码开源、品牌美术非商业授权”，不是所有内容都采用同一许可证，也不应把整个仓库连同美术统一宣传为 Apache-2.0。

## 参与贡献

提交 Issue 或 PR 前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md) 与 [SECURITY.md](SECURITY.md)。完整流程和边界见[架构文档](docs/ARCHITECTURE.zh-CN.md)。

## 状态

当前版本：`0.9.14`，定位仍是可运行 Demo，不建议直接作为面向公众的托管服务后端。
