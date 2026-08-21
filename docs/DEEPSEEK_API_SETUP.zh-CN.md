# DeepSeek API 注册与配置

DeepSeek 用于智能结构化、翻译和会议报告。只做原始语音转文字时可以不配置；会议报告功能需要配置。

## 1. 注册和充值

1. 打开 [DeepSeek 开放平台](https://platform.deepseek.com/)。
2. 登录并按平台要求完成账户设置。
3. 在平台查看余额与当前计费规则，先充值小额测试金额。
4. 打开 [API Key 管理](https://platform.deepseek.com/api_keys)。
5. 创建一枚只用于速说速记X测试的 API Key，并立即保存。

官方说明：

- [首次 API 调用](https://api-docs.deepseek.com/quick_start/pricing-details-cny/)
- [模型与计费](https://api-docs.deepseek.com/quick_start/pricing/)

## 2. 填入客户端

打开“连接设置”，填写：

```text
API Key     你的 DeepSeek API Key
模型        deepseek-v4-flash
Base URL    https://api.deepseek.com
```

截至 2026-08-21，项目默认使用 `deepseek-v4-flash`。供应商可能调整模型可用范围或模型名；遇到模型不存在时先核对官方文档和自己的账户权限，不要随意改成第三方代理地址。

## 3. 验证

### 智能结构化

1. 在主页开启“智能结构化”。
2. 用右 Alt 说一段包含三个要点的话。
3. 输出应去除部分语气词，并按 1、2、3 组织。

### 翻译

1. 开启“语言转换”。
2. 选择英语或日语。
3. 说一段中文，检查结果是否为目标语言。

### 会议报告

完成一次 1 分钟左 Alt 测试，报告页应先显示进度，随后出现可查看的历史报告。

## 4. 数据与费用

- 开启 AI 功能时，ASR 转写文字、提示模板和少量命中记忆会发送给 DeepSeek。
- 音频本身不发送给 DeepSeek。
- 更长的语音、更多上下文和 Thinking 总结通常会增加 token 和等待时间。
- 当前“上下文自动纠错”实验没有稳定带来额外准确率收益，因此不应默认开启；用户确认的本地词典仍是更可靠的专名纠错方式。

不要在截图、Issue 或演示视频中展示完整 API Key。怀疑泄漏时，立即到平台删除旧 Key 并创建新 Key。

