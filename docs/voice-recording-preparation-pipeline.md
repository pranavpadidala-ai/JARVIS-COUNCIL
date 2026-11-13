# Personal call-recording preparation pipeline

## User-provided source profile

- Collection: approximately 1,392 recordings.
- Container: M4A (audio codec, sample rate, channels and duration must be probed per file; M4A is a container, not a guarantee of one codec).
- Speakers: multiple people; the owner can identify their own voice in reference examples.
- Languages: English, Telugu, and Telugu-English code-switching.
- Processing target: local-first on a Windows laptop with 8 GB RAM and a low-power dual-core CPU. Heavy stages must be sequential, resumable and optionally offloaded only with explicit consent.

## Non-negotiable privacy and identity rules

1. Never upload original calls, extracted speech, embeddings or transcripts to GitHub.
2. Originals are read-only. All derived data lives in a user-selected private data directory outside the repository.
3. Do not treat diarisation as proof of identity. Speaker labels are provisional until the owner confirms reference samples and reviews uncertain matches.
4. Only verified owner speech may enter the personal voice dataset. Other participants are excluded unless their informed consent is explicitly recorded for the intended use.
5. Never clone another participant's voice. Do not infer personality, beliefs, health, relationships or other sensitive traits from calls.
6. Voice cloning and communication-style learning are separate opt-in outputs. A transcript can be used for language-profile learning only when the owner has reviewed the speaker attribution and is authorised to process it.
7. Keep a provenance trail: source file hash, segment time range, speaker decision, confidence, model/version, review status and exclusions.
8. Provide deletion by source recording, speaker, segment and derived dataset. Deleting a source must invalidate all dependent artifacts.

## Pipeline stages

### 0. Workspace and consent setup

Create a private workspace such as `%LOCALAPPDATA%/JARVIS/PrivateAudio/` (or a user-selected encrypted drive):

```text
PrivateAudio/
  originals/             # optional: user points to existing folder; never copied by default
  working/               # temporary decoded audio; safe to purge
  manifests/             # SQLite job database and JSON export
  references/            # owner-confirmed voice reference clips
  diarization/           # speaker turns and confidence
  transcripts/           # timestamped multilingual transcripts
  review/                # uncertain segments awaiting owner review
  datasets/
    owner_voice/         # owner-only, reviewed speech clips
    communication/       # reviewed owner text, language tags and provenance
  reports/
```

The app must not move or delete the source recordings. Encrypt sensitive derived data at rest where Windows DPAPI or an encrypted volume is available. Do not put private audio under the Git working tree.

### 1. Inventory and integrity scan

Recursively scan M4A files and probe each with FFprobe (or an equivalent local media probe). Capture:
- stable file ID and SHA-256;
- relative path, byte size, duration, codec, sample rate, channel count;
- decode/probe result and error details;
- exact duplicate groups by hash, and likely duplicates by duration/audio fingerprint.

Do not infer that all files are AAC just because the extension is M4A. Do not delete duplicates. Produce an inventory report and allow the user to exclude folders/files.

### 2. Decode and audio quality analysis

Decode one file at a time to temporary PCM WAV. Use mono 16 kHz for ASR/diarisation compatibility, while retaining the original M4A as the source of truth. Keep an original-rate/high-quality derivative only for selected voice-cloning segments if the chosen synthesizer requires it.

Measure clipping, silence, loudness, sample rate, channel imbalance and obvious corruption. Apply only conservative loudness normalisation; do not denoise or time-stretch the only copy. Record every transformation and allow reprocessing from the original.

### 3. Owner voice reference and speaker diarisation

Before batch processing, present a small set of candidate recordings/segments for the owner to mark:
- “This is me”;
- “This is another speaker”;
- “Uncertain / overlapping”.

Use several clean owner-confirmed examples across different calls and recording conditions to build a local speaker reference/embedding. The user confirmation is the identity anchor; automated speaker similarity is only a candidate-ranking signal.

Run local diarisation to produce speaker turns with start/end times and confidence. Reconcile speaker clusters across calls only when confidence is adequate. Never merge speakers solely because they share gender, accent or language. If speaker matching is uncertain, route the segment to review and exclude it from owner datasets by default.

### 4. Multilingual transcription

Run multilingual ASR on each diarised turn or suitable contiguous segment. Initial baseline: the existing Whisper multilingual capability in JARVIS. Benchmark a representative sample before processing the full archive:
- English-only speech;
- Telugu-only speech;
- Telugu-English switching within a sentence;
- names, numbers and engineering/market vocabulary;
- noisy calls, overlapping speech and different microphones.

Preserve the spoken language and script. Do not translate or “correct” Telugu-English switching into formal English. Store timestamps, language hints, ASR confidence where available, model/version and uncertain spans. Use a second Telugu-capable ASR candidate only if the benchmark demonstrates a material accuracy improvement and its licence permits the intended use.

### 5. Human review and speaker correction

Create a review queue ordered by risk:
1. uncertain owner-vs-other speaker attribution;
2. overlapping speech;
3. low-confidence transcription;
4. names, technical terms, numbers and code-switched phrases.

The UI should play the exact segment, show the timestamped transcript and allow correction of speaker, text, language and exclusion status. Corrections become the ground truth for later processing. Do not automatically add unreviewed segments to training data.

### 6. Owner-only segment extraction

Create the voice dataset only from segments meeting all of these conditions:
- speaker explicitly confirmed as the owner or matches a reviewed owner cluster at a strict threshold;
- no overlapping speaker;
- sufficient speech duration and intelligibility;
- no severe clipping, distortion or background speech;
- permitted for voice synthesis by the owner.

Keep each segment linked to its original file and time range. Do not concatenate unrelated sentences into misleading training examples. Keep a validation split by call/session, not random adjacent clips, to reduce leakage.

### 7. Two distinct outputs

**A. Voice synthesis dataset**
- Clean owner-only audio and exact transcript.
- Language tags: English, Telugu, mixed.
- Preserve prosody and natural speech; do not aggressively denoise.
- Keep train/validation/test groups separated by source call.
- Start with reference-audio voice cloning/inference, not full fine-tuning. Full model fine-tuning is a later optional experiment after licensing, data quality and consent checks.

**B. Communication and English-learning dataset**
- Only reviewed owner-authored speech/transcripts.
- Keep original utterance, language spans, corrected transcript (if reviewed), and source provenance.
- Derive a modest editable profile: preferred vocabulary, recurring phrasing, English grammar patterns, code-switching, technical terminology and explicit corrections/preferences.
- Distinguish “how the owner naturally speaks” from “how JARVIS should write/respond”. Never treat disfluencies as errors to imitate unless explicitly requested.
- Do not infer personality or private beliefs. Avoid retaining sensitive call content when only language style is needed.

### 8. Resource management and resumability

The 8 GB laptop must not load ASR, diarisation and TTS models simultaneously. Use a sequential worker with configurable batch size (default one recording), a single model loaded at a time, bounded CPU threads and pause/resume. Persist stage state in SQLite so a crash does not restart completed files. Provide estimated remaining work only after durations and hardware throughput have been measured.

Prefer a two-pass strategy: inexpensive inventory/quality scan for all files, then a small representative pilot (for example 20–30 recordings) for ASR/diarisation evaluation, followed by full processing only after the user reviews the pilot. The count of 1,392 files alone is not enough to estimate processing time; total audio duration is required.

### 9. Reports and acceptance criteria

The first usable release must:
- inventory all readable files and report unreadable files without modifying originals;
- detect exact duplicates;
- resume safely after interruption;
- show per-file progress and errors;
- keep private data outside the repository;
- retain timestamps and language tags;
- send uncertain speaker segments to review rather than owner datasets;
- export a manifest and a human-readable quality report;
- support exclusion and deletion with derived-artifact invalidation.

The pipeline is not considered voice-training-ready until the owner reviews a sample of extracted clips from every language category and the false-owner-inclusion rate is acceptably low.

## Initial configuration

```json
{
  "input": {
    "extensions": [".m4a"],
    "recursive": true,
    "preserveOriginals": true,
    "copyOriginals": false
  },
  "languages": ["en", "te", "en-te-mixed"],
  "processing": {
    "localOnly": true,
    "batchSize": 1,
    "resumeEnabled": true,
    "asrSampleRateHz": 16000,
    "asrChannels": 1
  },
  "identity": {
    "requireOwnerReferenceSamples": true,
    "requireHumanReviewForUncertainMatches": true,
    "excludeUnknownSpeakersByDefault": true,
    "excludeOverlappingSpeechFromVoiceDataset": true
  },
  "outputs": {
    "voiceDatasetEnabled": false,
    "communicationDatasetEnabled": false,
    "requireReviewBeforeExport": true
  }
}
```

## One required setup step when the user is ready

The owner must choose the folder containing the M4A files and confirm a few short reference clips that clearly contain their own voice. No files are scanned until the user selects the folder in the local JARVIS app. The app should then offer a pilot batch before processing the full collection.
```


## Duration-aware scheduling (confirmed collection profile)

The owner reports that most recordings are shorter than 15 minutes, with some extending to a maximum of approximately 1.5 hours. Exact durations are not yet inventoried, so do not publish a total-hours or completion-time estimate until the scan completes.

- Probe duration first and bucket jobs by duration: short (<15 min), medium (15–45 min), long (45–90 min), and exception (>90 min).
- Process short files first to validate the pipeline and produce early review samples. Do not discard or permanently deprioritise long files.
- For diarisation/ASR, process long recordings in bounded windows (initial target 5–10 minutes with a small overlap, configurable after pilot evaluation). Merge adjacent-window speaker clusters only with evidence; preserve global timestamps by adding each window's source offset.
- Avoid cutting at arbitrary word boundaries where possible. Use voice activity detection / speech boundaries to align windows, while retaining a small overlap to avoid losing words at edges.
- Persist a per-file, per-window stage ledger. If a job is interrupted, resume from the last completed window/stage rather than restarting the full recording.
- Keep concurrency conservative: one decode/ASR/diarisation heavy task at a time on the 8 GB laptop. The scheduler may inventory and hash files without loading neural models.
- Report counts and total durations by bucket, estimated remaining work based on measured pilot throughput, and separate decode, diarisation, transcription, and review progress.
- Long-call speaker drift and participant changes are high-risk; route uncertain speaker re-identification to manual review. Do not assume a speaker ID remains stable across the entire 90-minute call.
- Pilot the window size and overlap on representative short, mixed-language and long calls before processing the full archive. A starting overlap of 1–2 seconds is a tuning parameter, not a guarantee of perfect boundary continuity.
