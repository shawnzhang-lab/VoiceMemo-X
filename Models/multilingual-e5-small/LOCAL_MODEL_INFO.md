# multilingual-e5-small local model

- Product: 速说速记X
- Upstream: `intfloat/multilingual-e5-small`
- Revision: `614241f622f53c4eeff9890bdc4f31cfecc418b3`
- License declared by upstream: MIT
- Runtime format: ONNX, general CPU-compatible export plus a local INT8 dynamic-quantized CPU variant
- Embedding dimensions: 384
- Max input length: 512 tokens
- Full model SHA-256: `ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665`
- Local INT8 model SHA-256: `739c8f25bbe6d8a6001cd2f048701da9879140cc67d4e9327716111e869dd717`

## Intended integration

This directory is the expected location of the optional multilingual embedding model used by `LocalEmbeddingService`. The public Git repository keeps only metadata; weights and tokenizer binaries must be obtained separately under the upstream license. If the files are missing, the application disables semantic-vector retrieval and continues with non-vector local memory.

Required retrieval conventions from the upstream model card:

- Prefix a live search query with `query: `.
- Prefix a stored memory with `passage: `.
- Apply attention-mask-aware average pooling to the last hidden state.
- L2-normalize the resulting 384-dimensional vector before cosine similarity search.

## Files

- `model.onnx`: CPU-compatible ONNX weights.
- `model-int8.onnx`: local per-channel INT8 dynamic quantization used by the app to reduce memory and package size.
- `tokenizer.json`, `sentencepiece.bpe.model`: multilingual tokenizer data.
- `config.json`, `tokenizer_config.json`, `special_tokens_map.json`: model and tokenizer configuration.
- `pooling_config.json`, `sentence_bert_config.json`: Sentence Transformers pooling configuration.
- `MODEL_CARD.md`: upstream model card and license declaration.

Source: https://huggingface.co/intfloat/multilingual-e5-small
