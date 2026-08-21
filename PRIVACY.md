# 隐私说明 / Privacy Notice

最后更新：2026-08-21

速说速记X 是一个在用户电脑上运行的 BYOK（自带密钥）Demo。项目维护者不提供中转服务器，也不会通过本项目仓库自动收集遥测；但你配置的第三方云服务会收到完成相应功能所需的数据。

## 数据流向

| 功能 | 发送的数据 | 接收方 |
|---|---|---|
| 日常语音输入 | 麦克风音频 | 腾讯云 ASR |
| 实时会议 | 麦克风与电脑播放音频 | 腾讯云 ASR / Speaker 2.0 |
| 导入音视频 | 解码后的音频 | 腾讯云 ASR / Speaker 2.0 |
| AI 整理/翻译 | ASR 文字、少量命中偏好/历史 | DeepSeek API |
| 会议总结 | 会议转写与用户模板 | DeepSeek API |

关闭 AI 整理后，日常输入不会把转写文字发送给 DeepSeek；会议总结和导入报告仍需要 DeepSeek。

## 本地保存

Windows 数据位于 `%LOCALAPPDATA%\VoiceMemoryDemo`：

- `settings.dat`：DPAPI 加密的 API 配置；
- `voice-memory.db`：纠错词典、偏好、历史和向量；
- `Meetings\`：Markdown 报告；
- `diagnostics.log`：运行诊断；
- `speaker-router.jsonl`：本地路由器审计记录。

剪贴板竞争日志可以包含占用程序的进程名、PID、路径与窗口标题，但不会记录剪贴板正文。日志、报告和数据库可能仍包含敏感上下文，应由用户自行保护和清理。

## 保留与删除

本项目没有后台定时上传或远程删除机制。删除报告卡片会请求确认；确认后将对应 Markdown 报告移入 Windows 回收站。卸载/删除应用程序不会自动清理本地数据。完整删除请退出应用后移除 `%LOCALAPPDATA%\VoiceMemoryDemo`。

第三方服务的保留期限、训练政策和合规区域不受本项目控制，请阅读腾讯云与 DeepSeek 的现行隐私和数据处理条款。

## 会议录制责任

用户负责遵守所在地关于录音、隐私、商业秘密和跨境数据的法律。开始录制前应明确告知并取得参与者同意。不要把受监管、医疗、法律或高度机密会议交给未经组织批准的第三方云服务。

## Security boundary

DPAPI protects secrets at rest for the current Windows account. It does not protect runtime secrets from malware or another process running as the same user. A public multi-user product should use a backend proxy or short-lived credentials instead of distributing long-lived cloud keys.
