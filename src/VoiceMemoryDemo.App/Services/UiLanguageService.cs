using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace VoiceMemoryDemo.App.Services;

public static class UiLanguageService
{
    public const string Chinese = "zh-CN";
    public const string English = "en";

    private static int _initialized;
    private static readonly FontFamily ChineseTextFont = new("Microsoft YaHei UI");
    private static readonly FontFamily EnglishTextFont = new("Segoe UI Variable Text");
    private static readonly FontFamily EnglishDisplayFont = new("Segoe UI Variable Display");
    private static readonly FontFamily EnglishTechnicalFont = new("Bahnschrift");
    private static readonly Dictionary<string, string> ChineseToEnglish = new(StringComparer.Ordinal)
    {
        // App chrome and navigation
        ["速说速记X"] = "VoiceMemo X",
        ["右 Alt 日常输入 · 左 Alt 会议纪要"] = "Right Alt for dictation · Left Alt for meetings",
        ["小夜正在待命"] = "Xiaoye is ready",
        ["准备中"] = "Getting ready",
        ["完成"] = "Ready",
        ["开始输入"] = "Dictation",
        ["会议报告"] = "Meeting reports",
        ["记忆"] = "Memory",
        ["连接设置"] = "Connections",
        ["切换客户端、通知和悬浮框语言"] = "Switch the interface, notifications, and status overlay language",

        // Home page
        ["3 步完成语音输入"] = "Dictate in 3 steps",
        ["适用于 Codex、ChatGPT、浏览器、微信和文档等任意输入框。"] = "Works in Codex, ChatGPT, browsers, chat apps, documents, and other text fields.",
        ["不用复制粘贴"] = "No copy and paste",
        ["点一下输入框"] = "Click a text field",
        ["先把光标放到你想输入文字的位置"] = "Place the cursor where you want the text to appear.",
        ["按右 Alt 开始"] = "Press Right Alt",
        ["看到“请输入语音”后，请在 5 秒内开始说"] = "Start speaking within 5 seconds after “Speak now” appears.",
        ["再按右 Alt 结束"] = "Press Right Alt again",
        ["文字会自动整理并输入到刚才的位置"] = "Your speech is refined and inserted at the cursor.",
        ["当前状态"] = "Current status",
        ["按一下右 Alt，开始说话"] = "Press Right Alt to start",
        ["再按一次结束；5 秒内没有检测到语音会自动关闭。"] = "Press it again to stop. The session closes if no speech is heard within 5 seconds.",
        ["使用范围：聊天、网页、文档等任意输入框"] = "Works in chat apps, web pages, documents, and other text fields",
        ["开始和结束都按它"] = "Press to start and stop",
        ["功能与启动方式"] = "Features and shortcuts",
        ["两项功能均已就绪"] = "Both features are ready",
        ["开机启动"] = "Startup",
        ["登录 Windows 后自动运行"] = "Run automatically after Windows sign-in",
        ["当前未开启"] = "Currently off",
        ["开机自启"] = "Launch at startup",
        ["日常语音转文字"] = "Everyday dictation",
        ["开启语言转换"] = "Translate output",
        ["智能结构化"] = "Smart structure",
        ["先清理语气词和错词；检测到多个观点时自动按 1、2、3 分点"] = "Remove fillers and fix likely errors, then number multiple points automatically.",
        ["识别模式"] = "Recognition mode",
        ["默认自动识别中文普通话、英语和粤语；其他语言请选择对应语种"] = "Automatically detects Mandarin, English, and Cantonese. Select other languages manually.",
        ["自动（中 / 英 / 粤）"] = "Auto (Mandarin / English / Cantonese)",
        ["中文普通话"] = "Mandarin Chinese",
        ["英语 English"] = "English",
        ["粤语"] = "Cantonese",
        ["日语 日本語"] = "Japanese 日本語",
        ["韩语 한국어"] = "Korean 한국어",
        ["输出语言"] = "Output language",
        ["开启语言转换后生效"] = "Used when output translation is enabled",
        ["选择目标语言后会自动开启语言转换"] = "Choosing a target language automatically enables translation",
        ["中文"] = "Chinese",
        ["法语 Français"] = "French Français",
        ["德语 Deutsch"] = "German Deutsch",
        ["西班牙语 Español"] = "Spanish Español",
        ["俄语 Русский"] = "Russian Русский",
        ["开始 · 结束"] = "START · STOP",
        ["实时转写"] = "Live transcript",
        ["腾讯 ASR 返回的原始文字"] = "Raw text returned by Tencent ASR",
        ["整理 / 翻译后文字"] = "Refined / translated text",
        ["DeepSeek Flash + 你的本地记忆 + 目标语言"] = "DeepSeek Flash + local memory + target language",
        ["开始录音（也可以直接按右 Alt）"] = "Start dictation (or press Right Alt)",
        ["打开会议纪要文件夹"] = "Open meeting notes folder",

        // Legacy meeting controls kept for compatibility
        ["实时会议纪要"] = "Live meeting notes",
        ["关闭 · 仍使用普通语音输入模式"] = "Off · regular dictation remains available",
        ["开启后：麦克风 + 电脑播放声实时转写并标注说话人1/2/3 → Flash 分段提炼 → Thinking High 总结 → 保存 Markdown"] = "When enabled: microphone + computer audio → speaker labels → Flash segment notes → Thinking High summary → Markdown report",
        ["仅开启此模式时使用腾讯 2.0 说话人分离并产生对应用量；普通语音输入不受影响。"] = "Tencent Speaker 2.0 usage applies only to meeting mode. Regular dictation is unaffected.",
        ["同时收录电脑播放声音（飞书 / 微信 / 浏览器中的对方语音）"] = "Capture computer audio from meeting and browser apps",
        ["建议佩戴耳机，避免扬声器声音再次进入麦克风造成回声。"] = "Use headphones to prevent speaker audio from re-entering the microphone.",
        ["开启实时会议模式"] = "Enable live meeting mode",
        ["从音频 / 视频生成会议纪要（暂时保留）"] = "Create meeting notes from audio or video",
        ["选择 MP3、WAV、M4A、AAC、WMA、MP4、MOV 或 M4V；原文件不会被修改。"] = "Choose an MP3, WAV, M4A, AAC, WMA, MP4, MOV, or M4V file. The original is not modified.",
        ["导入会议音视频固定使用腾讯 Speaker 2.0，自动标注说话人并产生对应的说话人分离用量。"] = "Imported media uses Tencent Speaker 2.0 to label speakers and incurs diarization usage.",
        ["尚未选择文件 · 处理时间约等于媒体时长 + 总结时间"] = "No file selected · processing takes roughly the media duration plus summary time",
        ["选择文件并生成"] = "Choose file and create report",
        ["取消"] = "Cancel",

        // Meeting report page
        ["正在处理"] = "In progress",
        ["0 项"] = "0 tasks",
        ["导入音视频"] = "Import media",
        ["取消处理"] = "Cancel task",
        ["目前没有正在生成的报告"] = "No reports are being generated",
        ["历史报告"] = "Report history",
        ["正在读取本机报告…"] = "Loading local reports…",
        ["编辑模板"] = "Edit template",
        ["编辑新会议报告的结构与关注重点"] = "Edit the structure and focus of new meeting reports",
        ["刷新"] = "Refresh",
        ["删除这份报告"] = "Delete this report",
        ["查看  ›"] = "View  ›",
        ["还没有会议报告"] = "No meeting reports yet",
        ["完成一次实时会议后，报告会出现在这里。"] = "Complete a live meeting and its report will appear here.",
        ["← 返回"] = "← Back",
        ["复制全文"] = "Copy all",
        ["导出…"] = "Export…",
        ["打开报告文件夹"] = "Open reports folder",

        // Memory page
        ["表达偏好"] = "Writing preferences",
        ["告诉它你喜欢怎样表达。例如：工作消息要简洁，不要使用感叹号。"] = "Describe how you want to write, such as keeping work messages concise and avoiding exclamation marks.",
        ["添加"] = "Add",
        ["删除"] = "Delete",
        ["小夜还不知道你的表达偏好"] = "Xiaoye has not learned your writing preferences yet",
        ["在上方写下一条，例如“工作消息尽量简洁”。"] = "Add one above, for example: “Keep work messages concise.”",
        ["专有词纠错"] = "Correction dictionary",
        ["把容易听错的词固定下来。之后会先本地替换，再交给小模型整理。"] = "Save commonly misheard terms. Local replacement runs before AI refinement.",
        ["管理纠错词"] = "Manage corrections",
        ["查看、编辑或删除已经记住的纠错习惯"] = "View, edit, or delete saved corrections",
        ["经常识别成"] = "Often recognized as",
        ["你希望写成"] = "Replace with",
        ["保存这个词"] = "Save correction",
        ["写法示例"] = "Examples",
        ["“扣的可斯”  →  “Codex”"] = "“code ex”  →  “Codex”",
        ["“迪普西克”  →  “DeepSeek”"] = "“deep seek”  →  “DeepSeek”",

        // Connection page
        ["腾讯云实时 ASR"] = "Tencent Cloud Realtime ASR",
        ["负责把麦克风声音实时变成文字"] = "Converts microphone audio to text in real time",
        ["识别引擎"] = "Recognition engine",
        ["打开腾讯 ASR 控制台"] = "Open Tencent ASR console",
        ["API 密钥管理"] = "Manage API keys",
        ["DeepSeek 文本整理与翻译"] = "DeepSeek refinement and translation",
        ["负责去口头禅、修标点、套用表达偏好并翻译目标语言"] = "Removes fillers, fixes punctuation, applies preferences, and translates output",
        ["模型名（可替换）"] = "Model (replaceable)",
        ["API 地址"] = "API endpoint",
        ["启用 AI 文本整理"] = "Enable AI text refinement",
        ["把原文和成文保存在本机历史中"] = "Save raw and refined text in local history",
        ["打开 DeepSeek 平台"] = "Open DeepSeek platform",
        ["密钥只会用 Windows 当前账户加密后保存在本机。"] = "Keys are encrypted for the current Windows account and stored locally.",
        ["项目文件和日志里不会保存或显示你的 SecretKey。"] = "Your SecretKey is never stored in project files or displayed in logs.",
        ["启用 AI 时，本次文字和少量命中记忆会发送给 DeepSeek；音频不会。"] = "When AI is enabled, text and a small amount of relevant memory are sent to DeepSeek. Audio is not sent.",
        ["保存设置"] = "Save settings",

        // Correction dictionary dialog
        ["纠错词典"] = "Correction dictionary",
        ["维护小夜记住的专有词写法，转写时会优先本地纠正。"] = "Manage terms Xiaoye remembers. Local corrections run before refinement.",
        ["正在读取…"] = "Loading…",
        ["仅保存在这台电脑"] = "Stored only on this computer",
        ["已保存词条"] = "Saved corrections",
        ["点击编辑可把词条带到右侧修改"] = "Select Edit to modify an entry on the right.",
        ["编辑"] = "Edit",
        ["还没有纠错词"] = "No corrections yet",
        ["在右侧新增第一条，小夜会在转写前先进行本地替换。"] = "Add the first one on the right. Xiaoye will replace it locally before refinement.",
        ["确认删除这条纠错？"] = "Delete this correction?",
        ["确认删除"] = "Delete",
        ["编辑词条"] = "Edit correction",
        ["新增或修改一条本地纠错习惯"] = "Add or update a local correction",
        ["例如：“扣的可斯” → “Codex”"] = "Example: “code ex” → “Codex”",
        ["新增纠错"] = "Add correction",
        ["取消编辑"] = "Cancel editing",
        ["词条只保存在本机，不会上传原始内容"] = "Corrections stay on this computer; raw content is not uploaded",

        // Template and delete dialogs
        ["编辑总结模板"] = "Edit summary template",
        ["总结模板"] = "Summary template",
        ["定义新会议报告的章节、顺序和关注重点"] = "Define the sections, order, and focus of new meeting reports",
        ["仅用于新报告"] = "New reports only",
        ["模板会与完整转写一起发送给模型。你可以改变报告格式，但事实核验、数字校验和禁止虚构行动项等规则始终生效。"] = "The template is sent to the model with the full transcript. You may change the format, while fact checks, number checks, and the ban on invented action items always remain active.",
        ["请保留“## 核心摘要”，历史报告列表会读取这一节作为卡片概览。"] = "Keep the “## Core summary” heading. Report history uses this section for card previews.",
        ["还原默认模板"] = "Restore default",
        ["保存模板"] = "Save template",
        ["删除会议报告"] = "Delete meeting report",
        ["删除这份会议报告？"] = "Delete this meeting report?",
        ["删除后会移入 Windows 回收站"] = "The report will be moved to the Windows Recycle Bin",
        ["正在生成的会议任务不会被删除。确认后，历史列表和报告详情会立即更新。"] = "Active meeting tasks will not be deleted. The report list and detail view update immediately.",

        // Status overlay and common runtime states
        ["日常输入 · 右 Alt"] = "DICTATION · RIGHT ALT",
        ["会议纪要 · 左 Alt"] = "MEETING · LEFT ALT",
        ["快捷键"] = "HOTKEY",
        ["右 Alt"] = "Right Alt",
        ["左 Alt"] = "Left Alt",
        ["连接中"] = "Connecting",
        ["正在连接语音识别"] = "Connecting to speech service",
        ["正在连接会议识别"] = "Connecting to meeting service",
        ["请输入语音"] = "Speak now",
        ["会议录制中"] = "Recording meeting",
        ["转写中"] = "Refining text",
        ["正在整理文字"] = "Refining text",
        ["正在整理会议"] = "Building meeting report",
        ["已复制"] = "Copied",
        ["会议报告已复制"] = "Meeting report copied",
        ["未完成"] = "Not completed",
        ["已自动关闭"] = "Closed automatically",
        ["再按右 Alt 结束并输入"] = "Press Right Alt to stop and insert",
        ["同时记录麦克风与电脑声音"] = "Capturing microphone and computer audio",
        ["正在处理上一段"] = "Finishing the previous session",

        // Frequently displayed runtime copy
        ["空闲"] = "Ready",
        ["再次按同一个按键结束。"] = "Press the same shortcut again to stop.",
        ["处理中"] = "Processing",
        ["上一段内容正在收尾"] = "Finishing the previous session",
        ["完成后即可再次使用左右 Alt；会议报告进度可在“会议报告”查看。"] = "You can use either Alt shortcut again when this finishes. Meeting progress appears under Meeting reports.",
        ["文件处理中"] = "Processing file",
        ["正在生成会议纪要"] = "Creating meeting notes",
        ["可在“会议报告”查看进度或点击取消。"] = "Track progress or cancel under Meeting reports.",
        ["正在录音"] = "Recording",
        ["启动和结束必须使用同一组快捷键。"] = "Use the same shortcut to start and stop.",
        ["等待语音"] = "Waiting for speech",
        ["请在 5 秒内开始说话"] = "Start speaking within 5 seconds",
        ["没有检测到语音会自动关闭，不会连接腾讯 ASR。"] = "The session closes without connecting to Tencent ASR if no speech is detected.",
        ["5 秒内没有检测到语音"] = "No speech detected within 5 seconds",
        ["本次未连接腾讯 ASR，也不会产生 AI 整理用量。"] = "Tencent ASR was not connected and no AI refinement usage was incurred.",
        ["检测到语音，正在连接腾讯实时语音…"] = "Speech detected. Connecting to Tencent Realtime ASR…",
        ["开头的声音已在本地临时缓冲，不会丢失。"] = "The opening audio is buffered locally and will not be lost.",
        ["录音中"] = "Recording",
        ["正在听你说…"] = "Listening…",
        ["说完后再按一下右 Alt。"] = "Press Right Alt again when you finish.",
        ["正在记录并区分说话人…"] = "Recording and identifying speakers…",
        ["实时显示说话人1/2/3；结束后生成最终纪要。"] = "Speaker labels appear live; a final report is created when you stop.",
        ["正在完成转写…"] = "Finalizing the transcript…",
        ["随后会用你的记忆整理文字。"] = "Your local memory will then be used to refine the text.",
        ["随后将用 Thinking High 生成最终会议纪要。"] = "Thinking High will then create the final meeting report.",
        ["整理中"] = "Refining",
        ["正在按你的习惯处理文字…"] = "Applying your writing preferences…",
        ["DeepSeek V4 Flash 正在去口头禅、修正标点。"] = "DeepSeek V4 Flash is removing fillers and fixing punctuation.",
        ["回填中"] = "Inserting",
        ["正在把文字送回输入框…"] = "Inserting text into the active field…",
        ["已回填原文"] = "Raw text inserted",
        ["文字已输入，但 AI 整理暂时失败"] = "Text was inserted, but AI refinement failed",
        ["文字已经输入"] = "Text inserted",
        ["继续按右 Alt 可以开始下一段。"] = "Press Right Alt to start another dictation.",
        ["文字已经复制到剪贴板"] = "Text copied to the clipboard",
        ["未能回填"] = "Could not insert text",
        ["目标输入框或剪贴板暂时不可用"] = "The target field or clipboard is temporarily unavailable",
        ["连接重试中"] = "Reconnecting",
        ["网络有波动，正在重新连接腾讯实时语音…"] = "The network is unstable. Reconnecting to Tencent Realtime ASR…",
        ["已录下的开头仍保存在本地。"] = "The captured opening audio remains buffered locally.",
        ["总结中"] = "Summarizing",
        ["Thinking 正在生成最终会议纪要…"] = "Thinking is creating the final meeting report…",
        ["请稍候，完成后会自动保存并复制。"] = "The report will be saved and copied automatically when complete.",
        ["设置已保存"] = "Settings saved",
        ["连接信息已安全保存在本机"] = "Connection details were saved securely on this computer",
        ["现在可以切回任意输入框按右 Alt 测试。"] = "Switch to any text field and press Right Alt to test.",
        ["需要处理"] = "Action needed",
        ["这次没有完成"] = "This session did not complete",
        ["已开启 · 登录后自动运行"] = "On · runs after sign-in",
        ["已开启开机自动启动"] = "Launch at startup enabled",
        ["已关闭开机自动启动"] = "Launch at startup disabled",
        ["此设置只影响当前 Windows 账户。"] = "This setting applies only to the current Windows account.",
        ["需要补充"] = "More information needed",
        ["请把“经常识别成”和“希望写成”都填上"] = "Fill in both the recognized form and the preferred form",
        ["已记住"] = "Saved",
        ["专有词纠错已经保存在本机"] = "The correction was saved on this computer",
        ["下一次转写会自动套用。"] = "It will be applied to the next transcript.",
        ["模板已保存"] = "Template saved",
        ["已恢复默认会议总结模板"] = "Default meeting template restored",
        ["已更新会议总结模板"] = "Meeting template updated",
        ["从下一份会议报告开始使用，已有报告不会改变。"] = "This applies to new reports. Existing reports are unchanged.",
        ["已删除"] = "Deleted",
        ["会议报告已移入回收站"] = "Meeting report moved to the Recycle Bin",
        ["如果误删，可以从 Windows 回收站恢复。"] = "You can restore it from the Windows Recycle Bin.",
        ["正在读取报告…"] = "Loading report…",
        ["复制失败"] = "Copy failed",
        ["会议报告全文已复制"] = "Full meeting report copied",
        ["剪贴板暂时被其他程序占用"] = "The clipboard is temporarily in use by another app",
        ["可以粘贴到飞书、Word 或聊天窗口。"] = "You can paste it into a meeting app, Word, or a chat window.",
        ["导出会议报告"] = "Export meeting report",
        ["Markdown 文档|*.md|纯文本文件|*.txt"] = "Markdown document|*.md|Plain text file|*.txt",
        ["导出完成"] = "Export complete",
        ["会议报告已经导出"] = "Meeting report exported",
        ["纠错词已经更新。"] = "Correction updated.",
        ["新的纠错词已经保存。"] = "New correction saved.",
        ["这个识别写法已经存在，请直接编辑原有记录。"] = "This recognized form already exists. Edit the existing entry instead.",
        ["保存失败，请稍后重试。"] = "Could not save. Try again later.",
        ["已取消编辑。"] = "Editing cancelled.",
        ["纠错词已经删除。"] = "Correction deleted.",
        ["删除失败，请稍后重试。"] = "Could not delete. Try again later.",
        ["保存修改"] = "Save changes",
        ["已载入默认模板，点击“保存模板”后生效。"] = "Default template loaded. Select Save template to apply it.",
        ["界面语言已切换"] = "Interface language changed",
        ["客户端和悬浮提示已更新"] = "The app and status overlay have been updated",
        ["语言选择已经保存在本机，下次启动会继续使用。"] = "Your choice is saved on this computer and will be used next time.",

        // Meeting and import progress
        ["读取文件"] = "Reading file",
        ["读取文件中"] = "Reading file",
        ["分析人数"] = "Analyzing speakers",
        ["收尾中"] = "Finalizing",
        ["保存中"] = "Saving",
        ["步骤 1/6 · 读取文件"] = "Step 1/6 · Read file",
        ["步骤 2/6 · 本地人数判断"] = "Step 2/6 · Analyze speakers locally",
        ["步骤 3/6 · 建立 ASR 连接"] = "Step 3/6 · Connect to ASR",
        ["步骤 3/6 · 文件转写"] = "Step 3/6 · Transcribe file",
        ["步骤 4/6 · 转写收尾"] = "Step 4/6 · Finalize transcript",
        ["步骤 5/6 · AI 总结"] = "Step 5/6 · AI summary",
        ["步骤 6/6 · 保存报告"] = "Step 6/6 · Save report",
        ["步骤 1/5 · 音频收尾"] = "Step 1/5 · Finalize audio",
        ["步骤 2/5 · 最终转写"] = "Step 2/5 · Final transcript",
        ["步骤 3/5 · Flash 阶段摘要"] = "Step 3/5 · Flash segment notes",
        ["步骤 4/5 · Thinking 总结"] = "Step 4/5 · Thinking summary",
        ["步骤 5/5 · 准备保存"] = "Step 5/5 · Prepare to save",
        ["正在取消…"] = "Cancelling…",
        ["已取消"] = "Cancelled",
        ["已完成"] = "Completed",
        ["会议报告生成失败"] = "Meeting report failed",
        ["文件处理已取消"] = "File processing cancelled",
        ["没有生成新的会议报告。"] = "No new meeting report was created.",
        ["已停止处理文件"] = "File processing stopped",
        ["未生成纪要，原始文件没有被修改。"] = "No report was created. The original file was not modified.",
        ["正在准备本地说话人数分析…"] = "Preparing local speaker analysis…",
        ["当前是影子验证期，实际仍使用 Speaker 2.0。"] = "The router is in shadow mode; Speaker 2.0 is still used.",
        ["正在读取音视频文件，尚未发送到语音识别服务。"] = "Reading the media file. Nothing has been sent to the speech service yet.",
        ["本地判断：单人候选（影子验证，实际仍使用 Speaker 2.0）"] = "Local result: likely one speaker (shadow mode still uses Speaker 2.0)",
        ["本地判断：未发现可靠语音，已停止云端提交"] = "Local result: no reliable speech; cloud submission stopped",
        ["本地判断：多人或不确定，使用 Speaker 2.0"] = "Local result: multiple or uncertain speakers; using Speaker 2.0",
        ["文件会议纪要已保存"] = "Meeting report saved",
        ["文件会议纪要已保存并复制"] = "Meeting report saved and copied",
        ["正在生成导入文件的会议报告"] = "Creating a report from imported media",
        ["会议报告已经生成"] = "Meeting report created",
        ["录制中"] = "Recording",
        ["正在记录实时会议"] = "Recording live meeting",
        ["结束会议后会依次完成转写、阶段摘要、Thinking 总结和本地保存。"] = "After the meeting ends, the app finalizes the transcript, creates segment notes, runs Thinking, and saves locally.",
        ["正在整理会议内容"] = "Processing meeting content",
        ["会议已经结束，正在生成报告"] = "The meeting ended. Creating the report",
        ["正在停止麦克风和电脑声音采集，保留最后一句话。"] = "Stopping microphone and computer audio while preserving the final words.",
        ["没有检测到有效会议语音"] = "No valid meeting speech detected",
        ["没有检测到有效语音"] = "No valid speech detected",
        ["本次没有生成报告。"] = "No report was created.",
        ["按右 Alt 可以重新开始。"] = "Press Right Alt to try again.",
        ["正在确认完整转写"] = "Confirming the full transcript",
        ["音频已经收齐，正在等待腾讯 ASR 返回最后一句文字。"] = "All audio has been received. Waiting for Tencent ASR to return the final words.",
        ["转写完成，正在提炼会议重点"] = "Transcript complete. Extracting meeting highlights",
        ["正在整理会议片段"] = "Processing meeting segments",
        ["阶段摘要用于压缩长会议；最终结论仍以完整转写为准。"] = "Segment notes compress long meetings; the full transcript remains the source of truth.",
        ["AI 总结已经完成"] = "AI summary complete",
        ["正在把会议纪要和原始转写写入本机报告文件。"] = "Saving the meeting report and original transcript locally.",
        ["会议纪要已保存并复制"] = "Meeting report saved and copied",
        ["会议纪要已保存，剪贴板暂时不可用"] = "Meeting report saved; clipboard temporarily unavailable",

        // Configuration and friendly errors
        ["还没有配置腾讯云 ASR。请先到“连接设置”填写 AppID、SecretID 和 SecretKey。"] = "Tencent Cloud ASR is not configured. Enter the AppID, SecretID, and SecretKey under Connections.",
        ["还没有配置腾讯云 ASR。请先到“连接设置”填写 AppID、SecretID 和 SecretKey 并保存。 "] = "Tencent Cloud ASR is not configured. Enter and save the AppID, SecretID, and SecretKey under Connections.",
        ["导入文件生成会议纪要需要 DeepSeek API Key。"] = "A DeepSeek API key is required to create a meeting report from a file.",
        ["会议纪要模式需要 DeepSeek API Key，请先到“连接设置”完成配置。 "] = "Meeting mode requires a DeepSeek API key. Configure it under Connections.",
        ["腾讯 ASR 未授权：请开通语音识别服务，或给当前 SecretID 授予 QcloudASRFullAccess。"] = "Tencent ASR is not authorized. Enable the speech service or grant QcloudASRFullAccess to this SecretID.",
        ["腾讯实时说话人分离尚未开通：请进入腾讯 ASR 控制台开通实时语音识别服务。"] = "Tencent realtime speaker diarization is not enabled. Enable Realtime ASR in the Tencent console.",
        ["腾讯 ASR 可用额度已经耗尽：请在控制台开启后付费或充值后重试。"] = "Tencent ASR quota is exhausted. Enable postpaid billing or add funds and try again.",
        ["腾讯云账户欠费，实时说话人分离已停止：请充值后重试。"] = "The Tencent Cloud account has an overdue balance. Add funds before using speaker diarization again.",
        ["腾讯 ASR 会话结束异常，请重新说一次；若重复出现可查看本机诊断日志。"] = "The Tencent ASR session ended unexpectedly. Try again; check the local diagnostics log if it repeats.",
        ["Windows 剪贴板正被其他程序占用，文字已保留在主窗口，请稍后重试。"] = "Another app is using the Windows clipboard. The text remains in the main window; try again shortly.",
        ["DeepSeek API Key 验证失败，请检查 DeepSeek 连接设置。"] = "DeepSeek API key validation failed. Check the DeepSeek connection settings.",
        ["服务鉴权失败（HTTP 401），请在诊断日志中确认具体服务。"] = "Service authentication failed (HTTP 401). Check the diagnostics log for the affected service.",
        ["网络无法连接到服务，请检查网络或代理。"] = "Could not reach the service. Check the network or proxy.",
        ["连接腾讯 ASR 超时：当前网络可能正在切换或抖动，请确认网络稳定后重试。"] = "Tencent ASR connection timed out. Check the network and try again.",
        ["腾讯 ASR 连接没有成功建立，请重新尝试。"] = "Tencent ASR did not connect. Try again.",
        ["麦克风和电脑播放声都没有采集到声音。请检查 Windows 声音设备、麦克风权限以及会议软件的扬声器设置。"] = "No microphone or computer audio was captured. Check Windows audio devices, microphone permission, and the meeting app output device.",
        ["语音识别会话不存在。 "] = "The speech recognition session is unavailable.",
        ["会议整理会话不存在。 "] = "The meeting summary session is unavailable.",
        ["正在读取音视频文件…"] = "Reading media file…",
        ["正在本地判断说话人数（影子验证，不消耗云端额度）…"] = "Analyzing speakers locally (shadow mode, no cloud usage)…",
        ["本地判断为单人；影子期仍使用 Speaker 2.0"] = "Likely one speaker; shadow mode still uses Speaker 2.0",
        ["本地发现多人声纹特征；安全使用 Speaker 2.0"] = "Multiple speaker signatures found; using Speaker 2.0",
        ["本地未发现可靠语音；已停止，不消耗云端额度"] = "No reliable speech found; stopped with no cloud usage",
        ["本地判断不确定；安全使用 Speaker 2.0"] = "Speaker count uncertain; using Speaker 2.0",
        ["文件中没有检测到可靠语音，已停止处理，不会消耗腾讯云额度。"] = "No reliable speech was detected. Processing stopped with no Tencent Cloud usage.",
        ["Windows 无法解码这个文件。请先转换为 MP3、WAV、M4A 或 MP4 后再导入。"] = "Windows could not decode this file. Convert it to MP3, WAV, M4A, or MP4 and import it again.",
        ["正在连接腾讯 Speaker 2.0…"] = "Connecting to Tencent Speaker 2.0…",
        ["正在连接腾讯普通 ASR…"] = "Connecting to Tencent standard ASR…",
        ["正在转写文件…"] = "Transcribing file…",
        ["文件已读完，正在等待最终转写…"] = "File read complete. Waiting for the final transcript…",
        ["文件中没有识别到有效语音。"] = "No valid speech was recognized in the file.",
        ["Flash 阶段摘要已完成，Thinking 正在生成最终纪要…"] = "Flash segment notes complete. Thinking is creating the final report…",
        ["正在保存会议纪要…"] = "Saving meeting report…",
        ["找不到要导入的音视频文件。"] = "The media file could not be found.",
        ["请先在“连接设置”中配置腾讯云 ASR。"] = "Configure Tencent Cloud ASR under Connections first.",
        ["腾讯 ASR 音频队列已关闭。"] = "The Tencent ASR audio queue is closed.",
        ["腾讯实时语音连接失败，请稍后重试。 "] = "Tencent Realtime ASR could not connect. Try again later."
    };

    private static readonly Dictionary<string, string> EnglishToChinese =
        ChineseToEnglish
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);

    public static string CurrentLanguage { get; private set; } = Chinese;
    public static bool IsEnglish => string.Equals(CurrentLanguage, English, StringComparison.OrdinalIgnoreCase);
    public static FontFamily TextFont => IsEnglish ? EnglishTextFont : ChineseTextFont;
    public static FontFamily DisplayFont => IsEnglish ? EnglishDisplayFont : ChineseTextFont;
    public static FontFamily TechnicalFont => IsEnglish ? EnglishTechnicalFont : ChineseTextFont;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => LocalizeElement(sender as DependencyObject)),
            handledEventsToo: true);
    }

    public static void SetLanguage(string? language)
    {
        CurrentLanguage = string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Chinese;
        RefreshTypographyResources();
    }

    private static void RefreshTypographyResources()
    {
        if (Application.Current is null) return;
        Application.Current.Resources["UiTextFont"] = TextFont;
        Application.Current.Resources["UiDisplayFont"] = DisplayFont;
        Application.Current.Resources["UiTechnicalFont"] = TechnicalFont;
    }

    public static string Text(string chinese, string english) => IsEnglish ? english : chinese;

    public static string Translate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var dictionary = IsEnglish ? ChineseToEnglish : EnglishToChinese;
        if (dictionary.TryGetValue(value, out var translated)) return translated;

        if (IsEnglish)
        {
            if (value.StartsWith("读取会议报告失败：", StringComparison.Ordinal))
                return "Could not load meeting reports: " + value[9..];
            if (value.StartsWith("删除报告失败：", StringComparison.Ordinal))
                return "Could not delete report: " + value[7..];
            if (value.StartsWith("导出失败：", StringComparison.Ordinal))
                return "Export failed: " + value[5..];
            if (value.StartsWith("报告暂时无法读取：", StringComparison.Ordinal))
                return "Could not read this report: " + value[9..];
            if (value.StartsWith("这次会把文字输入到：", StringComparison.Ordinal))
                return "Text will be inserted into: " + value[10..];
            if (value.StartsWith("已保存：", StringComparison.Ordinal))
                return "Saved: " + value[4..];
            if (value.StartsWith("正在创建报告：", StringComparison.Ordinal))
                return "Creating report: " + value[7..];
            if (value.StartsWith("正在编辑：", StringComparison.Ordinal))
                return "Editing: " + value[5..];
            if (value.StartsWith("已获得 ", StringComparison.Ordinal) && value.EndsWith(" 个字符的完整转写。", StringComparison.Ordinal))
                return "Full transcript received: " + value[4..^10] + " characters.";
            if (value.StartsWith("阶段摘要 ", StringComparison.Ordinal))
                return value.Replace("阶段摘要 ", "Segment summaries: ", StringComparison.Ordinal)
                    .Replace(" 段 · 文件：", " · File: ", StringComparison.Ordinal);
            if (value.StartsWith("剪贴板被其他程序持续占用约 ", StringComparison.Ordinal))
                return value.Replace("剪贴板被其他程序持续占用约 ", "Another app held the clipboard for about ", StringComparison.Ordinal)
                    .Replace(" 毫秒；纪要文件仍完整保存：", " ms. The report is safely saved: ", StringComparison.Ordinal)
                    .Replace(" 毫秒；文件已完整保存：", " ms. The file is safely saved: ", StringComparison.Ordinal);
            if (value.StartsWith("正在转写 ", StringComparison.Ordinal))
                return TranslateDurationText(value.Replace("正在转写 ", "Transcribing ", StringComparison.Ordinal));
            if (value.StartsWith("已发送 ", StringComparison.Ordinal))
                return TranslateDurationText(value.Replace("已发送 ", "Sent ", StringComparison.Ordinal)
                    .Replace(" 音频…", " of audio…", StringComparison.Ordinal));
            if (value.StartsWith("本地判断", StringComparison.Ordinal) && value.Contains(" · 分析 ", StringComparison.Ordinal))
                return TranslateDurationText(value.Replace(" · 分析 ", " · analysis ", StringComparison.Ordinal));
            if (value.Contains(" · 用时 ", StringComparison.Ordinal))
            {
                var parts = value.Split(" · 用时 ", 2, StringSplitOptions.None);
                return $"{Translate(parts[0])} · {TranslateDurationText(parts[1])}";
            }
            if (value.StartsWith("连接暂时失败，正在重试（", StringComparison.Ordinal))
                return value.Replace("连接暂时失败，正在重试（", "Connection failed temporarily. Retrying (", StringComparison.Ordinal)
                    .Replace("）…", ")…", StringComparison.Ordinal);
            if (value.StartsWith("设备有声音，但腾讯没有识别到有效语音（", StringComparison.Ordinal))
                return value.Replace("设备有声音，但腾讯没有识别到有效语音（", "Audio was captured, but Tencent recognized no valid speech (", StringComparison.Ordinal)
                    .Replace("麦克风峰值 ", "microphone peak ", StringComparison.Ordinal)
                    .Replace("，电脑声音峰值 ", ", computer audio peak ", StringComparison.Ordinal)
                    .Replace("）；请连续播放或说一句完整的话后再结束。 ", "). Play or speak a complete sentence before stopping.", StringComparison.Ordinal);
            if (value.EndsWith(" · 已保存", StringComparison.Ordinal))
                return TranslateDurationText(value[..^6]) + " · saved";
        }

        return value;
    }

    private static string TranslateDurationText(string value) => value
        .Replace(" 小时 ", "h ", StringComparison.Ordinal)
        .Replace(" 分钟 ", "m ", StringComparison.Ordinal)
        .Replace(" 分钟", "m", StringComparison.Ordinal)
        .Replace(" 秒", "s", StringComparison.Ordinal);

    public static void Apply(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        ApplyRecursive(root, visited);
    }

    private static void ApplyRecursive(DependencyObject? current, HashSet<DependencyObject> visited)
    {
        if (current is null || !visited.Add(current)) return;
        LocalizeElement(current);

        foreach (var child in LogicalTreeHelper.GetChildren(current))
        {
            if (child is DependencyObject dependencyObject) ApplyRecursive(dependencyObject, visited);
        }

        if (current is not Visual && current is not Visual3D) return;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
        {
            ApplyRecursive(VisualTreeHelper.GetChild(current, index), visited);
        }
    }

    private static void LocalizeElement(DependencyObject? element)
    {
        if (element is null) return;
        if (element is FrameworkElement { Name: "UiLanguageButtonPrimaryText" or "UiLanguageButtonSecondaryText" }) return;

        if (element is Window window)
        {
            window.Title = Translate(window.Title);
            window.FontFamily = TextFont;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(window, TextHintingMode.Fixed);
        }
        if (element is TextBlock textBlock) textBlock.Text = Translate(textBlock.Text);
        if (element is TextBox textBox) textBox.Text = Translate(textBox.Text);
        if (element is Run run) run.Text = Translate(run.Text);
        if (element is ContentControl contentControl && contentControl.Content is string content)
            contentControl.Content = Translate(content);
        if (element is HeaderedContentControl headered && headered.Header is string header)
            headered.Header = Translate(header);

        if (element is FrameworkElement frameworkElement && frameworkElement.ToolTip is string toolTip)
            frameworkElement.ToolTip = Translate(toolTip);
    }
}
