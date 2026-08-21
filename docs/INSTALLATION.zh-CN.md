# 安装与运行

本页主要面向开发者从源码运行。普通用户请看[下载教程](DOWNLOAD_GUIDE.zh-CN.md)和 [Windows 安装教程](WINDOWS_INSTALL_GUIDE.zh-CN.md)。

## 1. 系统要求

- Windows 10/11 x64；
- .NET 8 SDK（源码运行）或项目发布的免安装包；
- 可用麦克风；会议模式还需要 Windows 正常播放远端声音；
- 腾讯云 ASR 与 DeepSeek API 账户。

当前 Windows 安装包尚未代码签名。若 SmartScreen 拦截，请先核对 Release 页面公布的 SHA-256；不要从第三方网盘下载来源不明的可执行文件。模型再分发许可未复核完成前，项目首次公开只提供源码，不应下载旧的本地测试包。

## 2. 从源码运行

```powershell
git clone https://github.com/shawnzhang-lab/VoiceMemo-X.git
cd VoiceMemo-X
dotnet restore .\VoiceMemoryDemo.sln
dotnet build .\VoiceMemoryDemo.sln -c Release --no-restore
.\run-demo.ps1
```

代码库不包含 ONNX 模型权重。缺少模型时应用仍能构建并使用普通语音输入，但会关闭本地语义向量检索，并让导入会议安全回退到 Speaker 2.0。

## 3. 首次配置

1. 在腾讯云语音识别控制台开通实时语音识别。
2. 创建一组最小权限的 API 凭证；个人 Demo 可临时使用 SecretID/SecretKey，公开产品不得内置主账号密钥。
3. 在 DeepSeek 平台创建 API Key，并充值少量余额。
4. 启动客户端，打开“连接设置”，填写并保存。
5. 切换到任意输入框，按右 Alt 完成一次短句测试。
6. 打开一个可播放声音的应用，按左 Alt 完成一次会议模式测试。

配置字段见 [CONFIGURATION.zh-CN.md](CONFIGURATION.zh-CN.md)，逐步注册教程见 [API_SETUP_INDEX.zh-CN.md](API_SETUP_INDEX.zh-CN.md)。

## 4. 发布 Windows 免安装包

```powershell
.\publish-win-x64.ps1
```

输出位于：

```text
publish\速说速记X-win-x64\
```

上传 GitHub Release 前应执行：

```powershell
.\scripts\Test-PublicRelease.ps1
```

逐项确认模型权重允许再分发后，可使用
`.\scripts\Build-WindowsRelease.ps1 -AcknowledgeModelLicenseReview` 生成 ZIP、依赖清单和 SHA-256。不要把 `publish/` 目录直接提交到源码仓库。

## 5. 卸载与清理

应用是免安装程序，删除程序目录不会删除个人数据。若要彻底清理，请先备份需要的报告，再手动删除：

```text
%LOCALAPPDATA%\VoiceMemoryDemo
```

其中包含 API 密钥的 DPAPI 密文、SQLite 记忆、会议报告和诊断日志。
