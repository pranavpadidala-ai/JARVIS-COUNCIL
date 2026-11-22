"""JARVIS dance coach: follow a reference dance video and get a match score.

The reference video plays on the left and the webcam on the right. MediaPipe Pose tracks both bodies,
and each moment is scored by how closely the user's arm and leg angles match the dancer's (0-100).
At the end it prints one JSON line with the average score, the best and weakest body parts and the
weakest moments. Nothing is recorded or uploaded.

Usage: python dance_coach.py --video "C:\\path\\dance.mp4" [--camera 0] [--start 0] [--seconds 60] [--mirror]
Press Q or Esc to stop.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time

# Joint angles compared, as (name, a, b, c): angle at b between a and c.
JOINTS = [
    ("left elbow", "left_shoulder", "left_elbow", "left_wrist"),
    ("right elbow", "right_shoulder", "right_elbow", "right_wrist"),
    ("left shoulder", "left_hip", "left_shoulder", "left_elbow"),
    ("right shoulder", "right_hip", "right_shoulder", "right_elbow"),
    ("left knee", "left_hip", "left_knee", "left_ankle"),
    ("right knee", "right_hip", "right_knee", "right_ankle"),
    ("left hip", "left_shoulder", "left_hip", "left_knee"),
    ("right hip", "right_shoulder", "right_hip", "right_knee"),
]
PART = {"elbow": "arms", "shoulder": "arms", "knee": "legs", "hip": "legs"}


def angle(a, b, c) -> float:
    ab = (a[0] - b[0], a[1] - b[1])
    cb = (c[0] - b[0], c[1] - b[1])
    norm = math.hypot(*ab) * math.hypot(*cb)
    if norm == 0:
        return 180.0
    return math.degrees(math.acos(max(-1.0, min(1.0, (ab[0] * cb[0] + ab[1] * cb[1]) / norm))))


def joint_angles(points: dict) -> dict:
    out = {}
    for name, a, b, c in JOINTS:
        if min(points[a][2], points[b][2], points[c][2]) >= 0.5:
            out[name] = angle(points[a], points[b], points[c])
    return out


def mirror(points: dict) -> dict:
    """Swap left and right so the user can mirror a dancer facing them."""
    swapped = {}
    for key, value in points.items():
        if key.startswith("left_"):
            swapped["right_" + key[5:]] = value
        elif key.startswith("right_"):
            swapped["left_" + key[6:]] = value
        else:
            swapped[key] = value
    return swapped


def score(ref: dict, user: dict) -> tuple[float | None, dict]:
    """0-100: 100 when every shared joint angle matches, 0 at 90 degrees average difference."""
    shared = [j for j in ref if j in user]
    if len(shared) < 3:
        return None, {}
    diffs = {j: abs(ref[j] - user[j]) for j in shared}
    avg = sum(diffs.values()) / len(diffs)
    return max(0.0, 100.0 * (1 - avg / 90.0)), diffs


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--video", required=True)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--start", type=float, default=0)
    parser.add_argument("--seconds", type=float, default=60)
    parser.add_argument("--mirror", action="store_true", help="mirror the dancer (default on when facing them)")
    args = parser.parse_args()

    import cv2
    import mediapipe as mp

    names = {lm.name.lower(): lm.value for lm in mp.solutions.pose.PoseLandmark}
    video = cv2.VideoCapture(args.video)
    if not video.isOpened():
        print(json.dumps({"ok": False, "error": "Could not open the dance video."}))
        return 1
    fps = video.get(cv2.CAP_PROP_FPS) or 30
    video.set(cv2.CAP_PROP_POS_MSEC, args.start * 1000)
    cam = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW)
    if not cam.isOpened():
        print(json.dumps({"ok": False, "error": "Could not open the webcam. Is another app using it?"}))
        return 1

    def points_of(result):
        lms = result.pose_landmarks.landmark
        return {n: (lms[i].x, lms[i].y, lms[i].visibility) for n, i in names.items()}

    title = "JARVIS dance coach - press Q to stop"
    scores: list[tuple[float, float]] = []  # (video time, score)
    part_diffs: dict[str, list[float]] = {"arms": [], "legs": []}
    started = time.time()
    pose_ref = mp.solutions.pose.Pose(model_complexity=0)
    pose_user = mp.solutions.pose.Pose(model_complexity=0)
    try:
        while time.time() - started < args.seconds:
            # Keep the reference in real time: skip video frames if processing falls behind.
            target = args.start + (time.time() - started)
            while video.get(cv2.CAP_PROP_POS_MSEC) / 1000 < target - 1 / fps:
                if not video.grab():
                    break
            ok_v, ref_frame = video.read()
            ok_c, user_frame = cam.read()
            if not ok_v or not ok_c:
                break
            user_frame = cv2.flip(user_frame, 1)
            ref_frame = cv2.resize(ref_frame, (480, 360))
            user_frame = cv2.resize(user_frame, (480, 360))
            r = pose_ref.process(cv2.cvtColor(ref_frame, cv2.COLOR_BGR2RGB))
            u = pose_user.process(cv2.cvtColor(user_frame, cv2.COLOR_BGR2RGB))
            text = "Step back so your whole body is visible"
            if r.pose_landmarks and u.pose_landmarks:
                ref_points = points_of(r)
                s, diffs = score(joint_angles(mirror(ref_points) if args.mirror else ref_points), joint_angles(points_of(u)))
                if s is not None:
                    scores.append((target, s))
                    for joint, d in diffs.items():
                        part_diffs[PART[joint.split()[-1]]].append(d)
                    worst = max(diffs, key=diffs.get)
                    text = f"Match {s:.0f}%  -  watch your {worst}" if s < 75 else f"Match {s:.0f}%  -  great!"
                mp.solutions.drawing_utils.draw_landmarks(ref_frame, r.pose_landmarks, mp.solutions.pose.POSE_CONNECTIONS)
                mp.solutions.drawing_utils.draw_landmarks(user_frame, u.pose_landmarks, mp.solutions.pose.POSE_CONNECTIONS)
            view = cv2.hconcat([ref_frame, user_frame])
            cv2.rectangle(view, (0, 0), (960, 34), (0, 0, 0), -1)
            cv2.putText(view, text, (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            cv2.imshow(title, view)
            if (cv2.waitKey(1) & 0xFF) in (ord("q"), 27) or cv2.getWindowProperty(title, cv2.WND_PROP_VISIBLE) < 1:
                break
    finally:
        pose_ref.close()
        pose_user.close()
        video.release()
        cam.release()
        cv2.destroyAllWindows()

    if not scores:
        print(json.dumps({"ok": True, "scored": False}))
        return 0
    avg = sum(s for _, s in scores) / len(scores)
    # Weakest 5-second windows, to tell the user which part of the routine to practise.
    windows: dict[int, list[float]] = {}
    for t, s in scores:
        windows.setdefault(int(t // 5) * 5, []).append(s)
    weakest = sorted(((sum(v) / len(v), k) for k, v in windows.items()))[:3]
    parts = {p: (sum(v) / len(v)) for p, v in part_diffs.items() if v}
    print(json.dumps({
        "ok": True, "scored": True, "average": round(avg, 1), "seconds": round(time.time() - started),
        "weaker_part": max(parts, key=parts.get) if parts else None,
        "weakest_moments": [{"at_seconds": k, "score": round(s)} for s, k in weakest],
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
