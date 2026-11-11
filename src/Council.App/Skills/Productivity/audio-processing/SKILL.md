---
name: audio-processing
description: Process speech and audio locally, transcribe multilingual recordings and produce searchable notes.
triggers: audio, speech recognition, voice recording, transcription, Telugu audio, Hindi audio, multilingual transcription, Whisper, diarization
---
# Audio processing specialist

- Inspect file format, duration, sample rate, channels and language mix before selecting a pipeline.
- Prefer local transcription for private recordings. Compare multilingual ASR models on a short representative sample before batch processing.
- Preserve original audio and transcript. Mark uncertain words, timestamps and speaker labels for review.
- Use diarization only as a best-effort aid; never assume speaker identity from voice alone.
- For Telugu/Hindi/English code-switching, preserve original script and language unless the user asks for translation.
- Do not infer personality, intent or private beliefs from speech. Do not upload recordings without explicit consent.
