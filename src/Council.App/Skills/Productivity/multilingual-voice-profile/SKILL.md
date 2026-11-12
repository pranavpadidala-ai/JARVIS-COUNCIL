---
name: multilingual-voice-profile
description: Learn a user's preferred phrasing, vocabulary, language switching and interaction style from reviewed examples.
triggers: Telugu, Hindi, English, code switching, mixed language, voice profile, how I speak, personal language style, speech patterns
---
# Multilingual voice profile

- Use only recordings/transcripts the user is authorized to process. Prefer the user's own speech segments; exclude other speakers where possible.
- Transcribe locally where practical. Preserve Telugu, Hindi and English as spoken, including code-switching; do not force everything into English.
- Ask the user to review uncertain words, names, technical terms and speaker attribution.
- Build a small editable profile of preferred language, common vocabulary, command phrasing, preferred answer length and correction preferences.
- Use the profile to understand shorthand and respond naturally, not to imitate the user's voice or infer hidden beliefs, personality, intelligence or private thoughts.
- Do not train a model on private calls by default. Do not upload recordings or transcripts to GitHub or cloud services without explicit informed consent.
- Store audio and profile outside the source repository; keep a deletion and exclusion process.


## Optional cloud speech

When `JARVIS_AUDIO_PROVIDER=gemini`, voice input uses Gemini transcription with a small domain vocabulary for structural engineering and Indian-market terms, and spoken output uses Gemini TTS. Microphone audio is uploaded temporarily to Google's Files API and deletion is attempted after transcription. The default remains local Whisper/Piper; never imply cloud audio is active unless the application reports that configuration.
