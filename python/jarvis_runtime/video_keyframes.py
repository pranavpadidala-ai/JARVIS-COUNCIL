"""Save the key frames of a video (one per visible change) for OCR, so JARVIS can turn tutorials and
screen recordings into step-by-step notes.

Usage: python video_keyframes.py <video> <out_folder> [--every 2] [--max 30]
Prints one JSON line: {"ok": true, "duration": s, "frames": [{"at": s, "file": path}, ...]}
"""
from __future__ import annotations

import argparse
import json
import os
import sys


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("video")
    parser.add_argument("out")
    parser.add_argument("--every", type=float, default=2.0, help="seconds between samples")
    parser.add_argument("--max", type=int, default=30)
    parser.add_argument("--threshold", type=float, default=0.25, help="0-1, how different a frame must be")
    args = parser.parse_args()

    import cv2

    cap = cv2.VideoCapture(args.video)
    if not cap.isOpened():
        print(json.dumps({"ok": False, "error": "Could not open the video."}))
        return 1
    fps = cap.get(cv2.CAP_PROP_FPS) or 25
    duration = (cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0) / fps
    os.makedirs(args.out, exist_ok=True)

    frames, previous, t = [], None, 0.0
    while len(frames) < args.max and (duration == 0 or t <= duration):
        cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
        ok, frame = cap.read()
        if not ok:
            break
        small = cv2.cvtColor(cv2.resize(frame, (160, 90)), cv2.COLOR_BGR2GRAY)
        hist = cv2.calcHist([small], [0], None, [32], [0, 256])
        cv2.normalize(hist, hist)
        changed = previous is None or cv2.compareHist(previous, hist, cv2.HISTCMP_BHATTACHARYYA) > args.threshold
        if changed:
            h, w = frame.shape[:2]
            if w > 1600:  # Windows OCR has a size limit; 1600 px wide keeps text readable
                frame = cv2.resize(frame, (1600, int(h * 1600 / w)))
            path = os.path.join(args.out, f"frame_{int(t):05d}.png")
            cv2.imwrite(path, frame)
            frames.append({"at": round(t, 1), "file": path})
            previous = hist
        t += args.every
    cap.release()
    print(json.dumps({"ok": True, "duration": round(duration, 1), "frames": frames}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
