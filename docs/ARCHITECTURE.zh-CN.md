# 整体架构

## 日常语音输入

```mermaid
flowchart LR
    A["右 Alt"] --> B["本地麦克风 + 5 秒语音门"]
    B -->|"无有效语音"| C["自动关闭，不产生云端调用"]
    B -->|"检测到语音"| D["腾讯实时 ASR"]
    D --> E["本地纠错词典"]
    E --> F{"启用 AI 整理?"}
    F -->|"否"| G["原文或本地纠错结果"]
    F -->|"是"| H["SQLite 命中记忆 + DeepSeek 整理/翻译"]
    H --> G
    G --> I["恢复原目标窗口"]
    I --> J["优先文本注入，失败时剪贴板回退"]
```

关键点：

- 录音开始时保留约一秒的内存预缓冲，避免云连接阶段漏掉开头；
- 结束后等待腾讯返回最终句，再执行本地词典和可选的大模型整理；
- 剪贴板原内容会尽量恢复；竞争失败会写诊断日志，不记录剪贴板正文。

## 实时会议纪要

```mermaid
flowchart LR
    A["左 Alt"] --> B["默认麦克风"]
    A --> C["Windows WASAPI Loopback"]
    B --> D["混合/分帧"]
    C --> D
    D --> E["腾讯 Speaker 2.0"]
    E --> F["匿名说话人分段"]
    F --> G["Flash 阶段摘要"]
    G --> H["Thinking 最终总结"]
    H --> I["Markdown 报告"]
    I --> J["本地历史、复制、导出、删除"]
```

客户端只能采集当前电脑实际收到的音频。手机上单独进行的会议、未在电脑播放的远端声音无法被捕获。

## 导入音视频

```mermaid
flowchart LR
    A["本地音视频"] --> B["Media Foundation 解码为 16k PCM"]
    B --> C["本地 VAD / 声纹路由影子判断"]
    C -->|"无可靠语音"| D["停止，不调用云端"]
    C -->|"有语音"| E["实际仍使用 Speaker 2.0"]
    E --> F["实时速率发送音频"]
    F --> G["最终会议报告"]
```

本地路由器目前对多人样本安全召回较好，但对真实单人样本过于敏感，节费价值没有达到发布门槛。因此 `ShadowModeEnabled=true`，不能宣称“自动选择更便宜 ASR”已经上线。

## 数据边界

| 数据 | 位置/去向 | 说明 |
|---|---|---|
| API 凭证 | `%LOCALAPPDATA%\VoiceMemoryDemo\settings.dat` | Windows DPAPI、当前用户范围 |
| 记忆与历史 | `voice-memory.db` | SQLite，本地保存 |
| 会议报告 | `Meetings\` | Markdown，本地保存 |
| 音频 | 腾讯云 ASR | 实时发送，不在当前客户端另存原始会议音频 |
| 转写与命中记忆 | DeepSeek（启用 AI 时） | 用于整理、翻译和总结 |
| 诊断 | `diagnostics.log` / `speaker-router.jsonl` | 不记录密钥与剪贴板正文 |

## 源码模块

- `MainWindow`：状态机、热键调度与 UI；
- `AudioRecorderService` / `LiveSpeechDetectionService`：录音和本地语音门；
- `TencentAsrSession`：签名、WebSocket、实时结果和说话人标签；
- `DeepSeekTextService`：整理、翻译和会议总结；
- `TextInjectionService`：目标窗口恢复、注入和剪贴板回退；
- `MemoryRepository` / `LocalEmbeddingService`：SQLite 与可选向量检索；
- `MeetingMediaImportService` / `MeetingMinutesService`：导入、分段和报告生成；
- `LocalSpeakerRouterService`：VAD、声纹聚类、重叠检测和安全路由建议。

## 威胁与产品化差距

当前是 BYOK 桌面 Demo。DPAPI 能防止密钥以明文落盘，但不能防止同一 Windows 用户下的恶意进程读取运行时内存。面向公众发布托管产品时，应增加后端代理、短期凭证、账户隔离、速率限制、审计、崩溃上报脱敏、代码签名与自动更新。
