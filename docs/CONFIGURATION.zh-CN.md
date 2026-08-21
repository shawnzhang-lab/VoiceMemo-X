# 配置说明

第一次配置建议从 [API 注册与配置索引](API_SETUP_INDEX.zh-CN.md)开始。本页是字段速查，不替代供应商开通教程。

## 腾讯云 ASR

控制台：

- [语音识别控制台](https://console.cloud.tencent.com/asr)
- [API 密钥管理](https://console.cloud.tencent.com/cam/capi)
- [实时语音识别计费说明](https://cloud.tencent.com/document/product/1093/35686)
- [普通 ASR 逐步配置](TENCENT_ASR_SETUP.zh-CN.md)
- [Speaker 2.0 逐步配置](TENCENT_SPEAKER_2_SETUP.zh-CN.md)

需要填写：

- `AppID`：腾讯云账号/项目的数字 AppID；
- `SecretID` 与 `SecretKey`：API 凭证；
- 普通识别引擎：默认 `16k_zh_en`；
- 会议说话人分离：客户端使用 `16k_zh_en_speaker_2.0`。

请使用最小权限凭证。不要把真实密钥写入源码、Issue、日志或截图。腾讯云价格和资源包会调整，以控制台实际账单为准。

## DeepSeek

- [API Key 管理](https://platform.deepseek.com/api_keys)
- 默认兼容地址：`https://api.deepseek.com`
- 当前默认模型标识：`deepseek-v4-flash`
- [DeepSeek 逐步配置](DEEPSEEK_API_SETUP.zh-CN.md)

实际可用模型名取决于账户和供应商当前接口。若模型名失效，应用会显示连接错误；不要未经验证就在 README 中承诺固定价格或永久免费额度。

## 语言与整理

- 识别模式可自动覆盖普通话、英语和粤语；
- 关闭“语言转换”时保留识别语言；
- 开启后可选择英语、日语等目标语言；
- “智能结构化”会去除语气词、修正常见错误并按逻辑分点；
- “编辑模板”只影响新生成的会议报告；事实核验、数字检查和禁止虚构行动项等底层约束不应被模板覆盖。

## 本地纠错与记忆

- 纠错词典：把常见误识别词映射到用户确认的写法；
- 表达偏好：保存语气、格式和称呼习惯；
- 历史记录：可选保存 ASR 原文与整理结果；
- 语义向量：本地模型存在时，将历史文本转换为 384 维向量用于相似度检索。

本地词典应优先于模型猜测。当前实验表明，直接让小模型自由决定专名纠错并不能稳定带来增益，因此上下文纠错功能不应默认开启或宣传为已完成。

## Clash/VPN 分流

腾讯 ASR 通常应直连中国大陆服务，避免跨境延迟和额外计费；DeepSeek 是否直连取决于所在地网络。分流规则属于用户网络配置，不应写死在客户端。可根据需要把 `asr.cloud.tencent.com` 配置为 `DIRECT`，并在日志中确认实际路由。
