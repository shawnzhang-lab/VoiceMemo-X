# Local speaker router models

These models are used only for local speaker-count risk routing. Audio is not uploaded by this stage.

- Segmentation: `sherpa-onnx-pyannote-segmentation-3-0/model.onnx`
  - Source: https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-segmentation-models
  - Bundled license: `sherpa-onnx-pyannote-segmentation-3-0/LICENSE`
- Speaker embedding used by the application: `3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx`
  - Source: https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models
  - Upstream implementation: https://github.com/modelscope/3D-Speaker
- Development reference model (not copied into the published app): `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx`
- Voice activity detection: `silero_vad.onnx`
  - Source: https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx
  - Upstream implementation: https://github.com/snakers4/silero-vad

The application intentionally starts in shadow mode: local predictions are logged, while imported media still uses Tencent Speaker 2.0 until the independent evaluation gates pass.

## Exact files used by the app

| File | SHA-256 |
|---|---|
| `silero_vad.onnx` | `9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6` |
| `3dspeaker_speech_campplus_sv_zh_en_16k-common_advanced.onnx` | `aa3cfc16963a10586a9393f5035d6d6b57e98d358b347f80c2a30bf4f00ceba2` |
| `sherpa-onnx-pyannote-segmentation-3-0/model.onnx` | `220ad67ca923bef2fa91f2390c786097bf305bceb5e261d4af67b38e938e1079` |

The public Git repository keeps only provenance and license metadata. Do not commit the weights. A binary/model-pack release must preserve the relevant upstream notices and re-check redistribution terms for each exact artifact.
