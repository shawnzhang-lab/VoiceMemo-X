# 腾讯云实时语音识别注册与配置

这部分负责右 Alt 的日常语音转文字。

## 1. 注册与实名认证

1. 打开[腾讯云注册页面](https://cloud.tencent.com/register)并登录。
2. 按控制台要求完成实名认证。
3. 打开[语音识别控制台](https://console.cloud.tencent.com/asr)，按页面提示开通语音识别服务。

腾讯云官方新手说明：[实时语音识别快速入门](https://cloud.tencent.com/document/product/1093/35689)。

## 2. 确认计费方式

打开：

- [语音识别计费说明](https://cloud.tencent.com/document/product/1093/35686)
- [语音识别资源包](https://cloud.tencent.com/document/product/1093/54408)

截至 2026-08-21，腾讯云文档列出的普通实时语音识别免费额度为每月 5 小时；大模型 2.0 不享受该免费额度。扣费通常按“免费额度 → 预付费资源包 → 后付费”顺序进行，新用户的后付费可能需要手动开启。不要把免费额度理解成速说速记X提供的额度。

先用小额充值/官方免费额度完成短句测试，再根据控制台真实用量决定是否购买资源包。不要为了 Demo 直接购买明显超出测试需求的大套餐。

## 3. 创建 API 密钥

1. 打开[API 密钥管理](https://console.cloud.tencent.com/cam/capi)。
2. 推荐先创建专用子账号，再为其创建密钥；参考[子账号访问密钥管理](https://cloud.tencent.com/document/product/598/37140)。
3. 为测试账号授予语音识别所需权限。当前 Demo 的报错提示兼容 `QcloudASRFullAccess`；正式产品应进一步收敛为最小权限策略。
4. 创建并安全保存 `SecretID` 和 `SecretKey`。

腾讯云当前只在创建时显示完整 SecretKey。关闭页面前先保存到可信密码管理器；不要复制到公开文档。

## 4. 找到 AppID

`AppID` 是腾讯云账号的数字应用标识，不是项目名称，也不是 SecretID。可在腾讯云控制台账号信息、API 密钥页面或 ASR 请求示例中查看。填写时只保留数字，不要加空格。

## 5. 填入客户端

打开“连接设置”，填写：

```text
AppID       你的数字 AppID
SecretID    API 密钥的 SecretID
SecretKey   与 SecretID 配对的 SecretKey
识别引擎    16k_zh_en
```

主页识别模式会根据中文/英文/粤语自动选择合适引擎；一般用户不要手工改成未验证的模型名。

## 6. 验证

1. 打开记事本并点击输入区。
2. 按右 Alt，等待“请输入语音”。
3. 说一句 5–10 秒的普通话或英语。
4. 再按右 Alt。
5. 在腾讯 ASR 控制台查看是否产生对应调用量。

若提示未授权、签名失败或欠费，依次检查 AppID 是否属于同一账号、SecretID/SecretKey 是否配对、账号权限、服务开通和余额。

接口参考：[实时语音识别 WebSocket API](https://cloud.tencent.com/document/api/1093/48982)。

