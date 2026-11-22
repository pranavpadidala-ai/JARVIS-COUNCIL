"""JARVIS webcam workout coach.

Counts reps for squats, push-ups, bicep curls, lunges and jumping jacks with MediaPipe Pose, gives
simple form tips on screen, and prints one JSON summary line when it ends. Everything stays on this
laptop: frames are never saved or sent anywhere.

Usage: python workout_coach.py --exercise squat --minutes 5 [--camera 0] [--target 20]
Press Q or Esc in the window to finish early.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time

EXERCISES = ("squat", "pushup", "curl", "lunge", "jumping_jack")


def angle(a, b, c) -> float:
    """Angle at b in degrees, from three (x, y) points."""
    ab = (a[0] - b[0], a[1] - b[1])
    cb = (c[0] - b[0], c[1] - b[1])
    dot = ab[0] * cb[0] + ab[1] * cb[1]
    norm = math.hypot(*ab) * math.hypot(*cb)
    if norm == 0:
        return 180.0
    return math.degrees(math.acos(max(-1.0, min(1.0, dot / norm))))


class RepCounter:
    """Two-state counter: a rep counts when the joint goes 'down' past one angle and back 'up' past another."""

    RULES = {
        # exercise: (down_when_below, up_when_above)
        "squat": (100, 160),
        "pushup": (95, 155),
        "curl": (50, 150),
        "lunge": (105, 160),
    }

    def __init__(self, exercise: str):
        self.exercise = exercise
        self.reps = 0
        self.down = False
        self.tip = ""

    def update(self, p) -> None:
        """p maps landmark names to (x, y, visibility)."""
        e = self.exercise
        if e == "jumping_jack":
            hands_up = p["left_wrist"][1] < p["nose"][1] and p["right_wrist"][1] < p["nose"][1]
            feet_wide = abs(p["left_ankle"][0] - p["right_ankle"][0]) > 1.6 * abs(p["left_hip"][0] - p["right_hip"][0])
            if hands_up and feet_wide:
                self.down = True
            elif self.down and not hands_up and not feet_wide:
                self.reps += 1
                self.down = False
            self.tip = "Arms fully overhead, feet wide" if not self.down else ""
            return

        side = "left" if p["left_hip"][2] + p["left_knee"][2] >= p["right_hip"][2] + p["right_knee"][2] else "right"
        g = lambda name: p[f"{side}_{name}"][:2]
        if e in ("squat", "lunge"):
            joint = angle(g("hip"), g("knee"), g("ankle"))
            back = angle(g("shoulder"), g("hip"), g("knee"))
            self.tip = "Keep your chest up" if self.down and back < 60 else ""
        elif e == "pushup":
            joint = angle(g("shoulder"), g("elbow"), g("wrist"))
            body = angle(g("shoulder"), g("hip"), g("ankle"))
            self.tip = "Keep your body in a straight line" if body < 155 else ""
        else:  # curl
            joint = angle(g("shoulder"), g("elbow"), g("wrist"))
            drift = abs(g("elbow")[0] - g("shoulder")[0])
            self.tip = "Keep your elbow close to your side" if drift > 0.12 else ""

        low, high = self.RULES[e]
        if joint < low:
            self.down = True
        elif joint > high and self.down:
            self.reps += 1
            self.down = False


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exercise", choices=EXERCISES, required=True)
    parser.add_argument("--minutes", type=float, default=5)
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--target", type=int, default=0)
    args = parser.parse_args()

    import cv2  # imported late so --help works without the packages
    import mediapipe as mp

    names = {lm.name.lower(): lm.value for lm in mp.solutions.pose.PoseLandmark}
    cap = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print(json.dumps({"ok": False, "error": "Could not open the webcam. Is another app using it?"}))
        return 1
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)

    counter = RepCounter(args.exercise)
    started = time.time()
    end = started + args.minutes * 60
    seen_frames = 0
    title = "JARVIS workout coach - press Q to finish"
    # model_complexity=0 is the light model, so an i3 without a GPU keeps up.
    with mp.solutions.pose.Pose(model_complexity=0, min_detection_confidence=0.5, min_tracking_confidence=0.5) as pose:
        while time.time() < end:
            ok, frame = cap.read()
            if not ok:
                break
            frame = cv2.flip(frame, 1)
            result = pose.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
            if result.pose_landmarks:
                seen_frames += 1
                lms = result.pose_landmarks.landmark
                points = {n: (lms[i].x, lms[i].y, lms[i].visibility) for n, i in names.items()}
                counter.update(points)
                mp.solutions.drawing_utils.draw_landmarks(frame, result.pose_landmarks, mp.solutions.pose.POSE_CONNECTIONS)
            left = int(end - time.time())
            goal = f" / {args.target}" if args.target else ""
            cv2.rectangle(frame, (0, 0), (640, 70), (0, 0, 0), -1)
            cv2.putText(frame, f"{args.exercise.replace('_', ' ').title()}: {counter.reps}{goal}", (12, 32),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
            cv2.putText(frame, counter.tip or ("Step back so your whole body is visible" if not result.pose_landmarks else f"{left // 60}:{left % 60:02d} left"),
                        (12, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1)
            cv2.imshow(title, frame)
            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27) or (args.target and counter.reps >= args.target):
                break
            if cv2.getWindowProperty(title, cv2.WND_PROP_VISIBLE) < 1:
                break
    cap.release()
    cv2.destroyAllWindows()
    minutes = round((time.time() - started) / 60, 1)
    print(json.dumps({"ok": True, "exercise": args.exercise, "reps": counter.reps, "minutes": minutes,
                      "body_seen": seen_frames > 0}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
