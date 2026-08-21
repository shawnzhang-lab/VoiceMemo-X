<p align="center">
  <img src="src/VoiceMemoryDemo.App/Assets/app-icon-xiaoye-v4.png" width="104" alt="VoiceMemo X icon">
</p>

<h1 align="center">VoiceMemo X / 速说速记X</h1>

<p align="center">Global voice dictation and live meeting notes for Windows.</p>

<p align="center">
  <a href="README.md">简体中文</a> ·
  <a href="docs/DOWNLOAD_GUIDE.zh-CN.md">Download</a> ·
  <a href="docs/WINDOWS_INSTALL_GUIDE.zh-CN.md">Installation</a> ·
  <a href="docs/API_SETUP_INDEX.zh-CN.md">API setup</a> ·
  <a href="docs/ARCHITECTURE.zh-CN.md">Architecture</a> ·
  <a href="PRIVACY.md">Privacy</a>
</p>

> [!IMPORTANT]
> This is an experimental bring-your-own-key project. It is not affiliated with Tencent Cloud, DeepSeek, Feishu, or OpenAI. Cloud ASR and LLM calls may incur third-party charges. Obtain consent from every participant before recording a meeting.

## How it works

### Right Alt — everyday dictation

Place the caret in any text field, press **Right Alt**, speak, then press **Right Alt** again. VoiceMemo X sends audio to Tencent Realtime ASR, optionally refines or translates the transcript with DeepSeek, applies the local correction dictionary, and inserts the result into the original field. A five-second local silence gate prevents accidental cloud calls.

### Left Alt — live meeting notes

Press **Left Alt** to capture the default microphone and Windows loopback audio. Press **Left Alt** again to stop. Tencent Speaker 2.0 provides anonymous speaker labels and DeepSeek generates a Markdown report that can be viewed, copied, exported, or deleted inside the app.

Imported audio/video also uses Speaker 2.0. The local speaker-count router remains in shadow mode and does not currently switch imports to standard ASR for cost savings.

## Features

- Global Windows hotkeys and separate animated overlays for dictation and meetings.
- Automatic Mandarin, English, and Cantonese recognition.
- Optional translation, filler removal, punctuation repair, smart lists, and editable prompts.
- Local SQLite memory, a correction dictionary, and optional local vector retrieval.
- Microphone plus WASAPI loopback capture and meeting-report history.
- Chinese and English UI/overlay localization.
- Windows DPAPI and Android Keystore credential storage.

## Build from source

Windows users can download `VoiceMemo-X-0.9.12-win-x64-lite.zip` and its matching `.sha256` file from [Releases](https://github.com/shawnzhang-lab/VoiceMemo-X/releases). The Lite archive contains no local ONNX model weights. Cloud dictation, translation, and meeting reports remain available, while local vector retrieval and the local speaker-count pre-check degrade gracefully.

Requirements: Windows 10/11 x64, the .NET 8 SDK, Tencent Cloud ASR credentials, and a funded DeepSeek API account.

```powershell
git clone https://github.com/shawnzhang-lab/VoiceMemo-X.git
cd VoiceMemo-X
dotnet restore .\VoiceMemoryDemo.sln
dotnet build .\VoiceMemoryDemo.sln -c Release --no-restore
.\run-demo.ps1
```

Optional local model weights are intentionally excluded from Git. The app still builds and regular dictation works without them; semantic-vector retrieval and the local speaker-count pre-check run in degraded mode. See [installation](docs/INSTALLATION.zh-CN.md) and [configuration](docs/CONFIGURATION.zh-CN.md). Do not upload the old locally built Windows QA archive or any binary containing unreviewed third-party model weights.

## Privacy and cost

- Audio is sent to Tencent Cloud ASR.
- When AI refinement is enabled, transcript text and a small amount of matched local memory are sent to DeepSeek.
- Windows secrets are encrypted with DPAPI for the current user.
- Reports, preferences, diagnostics, and the SQLite database stay under `%LOCALAPPDATA%\VoiceMemoryDemo` unless sent to the configured providers as described above.
- Uninstalling the app does not automatically erase that directory.

See [PRIVACY.md](PRIVACY.md) before use. Do not ship a Tencent main-account SecretKey inside a public binary; a consumer-facing service should use a backend proxy or short-lived credentials.

## Known limitations

- Audio-device selection is not yet exposed in the Windows UI.
- Elevated target applications may reject text injection; the text then remains on the clipboard.
- Speaker 2.0 has separate usage charges. The local router is not production-ready for cost routing.
- AI-generated meeting reports require human review.
- Windows binaries are currently unsigned and may trigger SmartScreen.
- The Android build is a validation demo and cannot inject text globally like the Windows client.

## Licensing boundary

Source code is Copyright 2026 Xiang Zhang and licensed under Apache-2.0. Xiaoye character/UI artwork is available only for personal and other non-commercial use under [ASSET_LICENSE.md](ASSET_LICENSE.md); commercial distributions must replace it. Model weights, private evaluation corpora, internal results, build outputs, and unused character candidates are excluded from Git. Third-party notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Current version: `0.9.12` — runnable demo, not a production hosted service.
