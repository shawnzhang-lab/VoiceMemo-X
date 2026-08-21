# Third-party notices

This file is an inventory, not a replacement for upstream license texts. Versions match the current project files and must be regenerated when dependencies change.

## Windows application packages

| Component | Version | License | Upstream |
|---|---:|---|---|
| Microsoft.Data.Sqlite | 10.0.10 | MIT | https://learn.microsoft.com/dotnet/standard/data/sqlite/ |
| Microsoft.ML.OnnxRuntime | 1.28.0 | MIT + bundled third-party notices | https://github.com/microsoft/onnxruntime |
| NAudio | 2.3.0 | MIT | https://github.com/naudio/NAudio |
| org.k2fsa.sherpa.onnx | 1.13.4 | Apache-2.0 | https://github.com/k2-fsa/sherpa-onnx |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | https://github.com/dotnet/runtime |
| Tokenizers.HuggingFace | 3.23.1 | Apache-2.0 | https://github.com/IgnaciodelaTorreArias/Tokenizers.HuggingFace.DotNet |

Binary releases should retain the license and third-party-notice files shipped by these packages, especially ONNX Runtime.

## Android application packages

| Component | Version | License |
|---|---:|---|
| Android Gradle Plugin | 8.7.3 | Apache-2.0 |
| AndroidX AppCompat | 1.7.0 | Apache-2.0 |
| OkHttp | 4.12.0 | Apache-2.0 |
| JUnit | 4.13.2 | Eclipse Public License 1.0 |

## Optional local models

Model weights are intentionally excluded from Git and are not covered by this project's Apache-2.0 license.

- `intfloat/multilingual-e5-small`: upstream model card identifies MIT; pinned source details are in `Models/multilingual-e5-small/LOCAL_MODEL_INFO.md`.
- Silero VAD, CAMPPlus speaker embedding, and sherpa-onnx pyannote segmentation artifacts: source and provenance are recorded in `Models/speaker-router/MODEL_INFO.md`.
- The included pyannote export directory keeps its upstream `LICENSE` file.

Before attaching a model pack to a GitHub Release, verify each exact file's license, redistribution permission, source revision, and SHA-256. Do not assume a code repository's license automatically covers model weights or training data.

## Cloud services and trademarks

Tencent Cloud, DeepSeek, Windows, Android, Feishu, WeChat, Teams, GitHub and other names are trademarks of their respective owners. Mentioning them describes compatibility only and does not imply endorsement.
