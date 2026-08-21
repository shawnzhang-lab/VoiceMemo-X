# API 注册与配置索引

速说速记X采用 BYOK（用户自带 API Key）。项目不会赠送腾讯云或 DeepSeek 额度，也不会代替供应商收费。

## 必需和可选服务

| 服务 | 是否必需 | 用途 | 教程 |
|---|---|---|---|
| 腾讯云实时语音识别 | 日常语音输入必需 | 把语音转成文字 | [TENCENT_ASR_SETUP.zh-CN.md](TENCENT_ASR_SETUP.zh-CN.md) |
| 腾讯 Speaker 2.0 | 会议/导入音视频必需 | 多说话人转写和匿名标签 | [TENCENT_SPEAKER_2_SETUP.zh-CN.md](TENCENT_SPEAKER_2_SETUP.zh-CN.md) |
| DeepSeek API | AI 整理、翻译和会议报告必需 | 结构化文字与总结 | [DEEPSEEK_API_SETUP.zh-CN.md](DEEPSEEK_API_SETUP.zh-CN.md) |

## 密钥安全

1. 优先创建腾讯云子账号和最小权限密钥，不要长期使用主账号密钥。
2. 不要把 SecretKey/API Key 发到 Issue、聊天群、截图或演示视频。
3. 不要把任何真实密钥写入源码或 Git。
4. 一旦怀疑泄漏，立即在供应商控制台禁用并重新创建。
5. 面向公众分发自己的商业产品时，不应把供应商密钥放进客户端；应改用自己的后端和短期凭证。

供应商的套餐、免费额度、模型名和价格可能变化。教程记录的是 2026-08-21 的核验结果，最终以官方控制台和计费文档为准。

