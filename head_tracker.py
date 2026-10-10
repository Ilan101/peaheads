import cv2, socket, json, math, sys
import mediapipe as mp

UDP_ADDR = ("127.0.0.1", 5052)
# Killcam face feed: small mirrored JPEGs (no overlay) to Unity, which keeps the last few seconds for the replay.
FACE_ADDR = ("127.0.0.1", 5053)
FACE_SIZE = (256, 144)
FACE_EVERY = 2  # every 2nd camera frame, ~15 fps
SMOOTH = 0.35            # head smoothing: higher = more responsive, lower = smoother

# ---- tuning knobs ----
EYE_CLOSED_RATIO = 0.50  # eye counts as closed below 50% of your open-eye value
MOUTH_OPEN = 0.30        # mouth-open ratio threshold (try 0.2 - 0.45)
AIM_FRAMES = 3           # frames a wink must hold before aim = True
SHOOT_FRAMES = 2         # frames mouth must stay open before shoot = True
BOX_FRAC = 0.30          # active box half-width as a fraction of the frame width (box edge = full deflection +-1)
                         # frame-relative, so camera mode / zoom can't shrink it; height follows 16:9

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
cap = cv2.VideoCapture(0, cv2.CAP_DSHOW) if sys.platform == "win32" else cv2.VideoCapture(0)  # DSHOW is Windows-only
# MJPG first: most webcams only manage ~10 fps at 720p uncompressed (YUYV), 30 fps compressed.
cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
cap.set(cv2.CAP_PROP_FPS, 30)

face_mesh = mp.solutions.face_mesh.FaceMesh(
    max_num_faces=1,
    refine_landmarks=True,   # better eyelid/lip accuracy
    min_detection_confidence=0.5,
    min_tracking_confidence=0.5,
)

# Eye landmarks: (outer, inner, top, bottom). A = left side of screen, B = right side.
EYE_A = (33, 133, 159, 145)
EYE_B = (263, 362, 386, 374)
# Mouth landmarks
LIP_TOP, LIP_BOTTOM, MOUTH_L, MOUTH_R = 13, 14, 61, 291

neutral = None            # (x, y, face_width, earA, earB)
sx = sy = None            # smoothed nose position
aim_count = 0
shoot_count = 0


def ratio(lm, idx, w, h):
    """Eye aspect ratio: vertical opening / horizontal width."""
    outer, inner, top, bottom = (lm[i] for i in idx)
    horiz = math.hypot((outer.x - inner.x) * w, (outer.y - inner.y) * h)
    vert = math.hypot((top.x - bottom.x) * w, (top.y - bottom.y) * h)
    return vert / horiz if horiz > 0 else 0.0


def mouth_ratio(lm, w, h):
    """Inner-lip gap / mouth width."""
    gap = math.hypot((lm[LIP_TOP].x - lm[LIP_BOTTOM].x) * w,
                     (lm[LIP_TOP].y - lm[LIP_BOTTOM].y) * h)
    width = math.hypot((lm[MOUTH_L].x - lm[MOUTH_R].x) * w,
                       (lm[MOUTH_L].y - lm[MOUTH_R].y) * h)
    return gap / width if width > 0 else 0.0


frame_index = 0
while True:
    ok, frame = cap.read()
    if not ok:
        break
    frame = cv2.flip(frame, 1)
    h, w = frame.shape[:2]
    frame_index += 1
    if frame_index % FACE_EVERY == 0:
        ok_jpg, jpg = cv2.imencode(".jpg", cv2.resize(frame, FACE_SIZE), [cv2.IMWRITE_JPEG_QUALITY, 60])
        if ok_jpg:
            sock.sendto(jpg.tobytes(), FACE_ADDR)
    result = face_mesh.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))

    packet = {"face": False, "dx": 0.0, "dy": 0.0, "aim": False, "shoot": False}

    if result.multi_face_landmarks:
        lm = result.multi_face_landmarks[0].landmark
        nose = lm[1]
        face_w = abs(lm[454].x - lm[234].x)

        # --- head position (smoothed) ---
        if sx is None:
            sx, sy = nose.x, nose.y
        sx += SMOOTH * (nose.x - sx)
        sy += SMOOTH * (nose.y - sy)

        # --- eyes and mouth ---
        ear_a = ratio(lm, EYE_A, w, h)
        ear_b = ratio(lm, EYE_B, w, h)
        mar = mouth_ratio(lm, w, h)

        if neutral is None:
            neutral = (sx, sy, face_w, ear_a, ear_b)

        nx, ny, nfw, n_ear_a, n_ear_b = neutral

        closed_a = ear_a < n_ear_a * EYE_CLOSED_RATIO
        closed_b = ear_b < n_ear_b * EYE_CLOSED_RATIO
        wink = closed_a != closed_b            # exactly one eye closed
        mouth_open = mar > MOUTH_OPEN

        aim_count = aim_count + 1 if wink else 0
        shoot_count = shoot_count + 1 if mouth_open else 0

        # active box in pixels, centred on the calibrated nose; offsets are in pixels too (aspect-correct)
        half_w = BOX_FRAC * w
        half_h = half_w * 9 / 16
        off_x = (sx - nx) * w
        off_y = (ny - sy) * h

        packet = {
            "face": True,
            "dx": max(-1.0, min(1.0, off_x / half_w)),
            "dy": max(-1.0, min(1.0, off_y / half_h)),
            "aim": aim_count >= AIM_FRAMES,
            "shoot": shoot_count >= SHOOT_FRAMES,
        }

        # --- debug overlay ---
        # active rectangle: your nose reaching its edge = full deflection (+-1)
        cx, cy = int(nx * w), int(ny * h)
        bw, bh = int(half_w), int(half_h)
        cv2.rectangle(frame, (cx - bw, cy - bh), (cx + bw, cy + bh), (255, 200, 0), 2)

        cv2.circle(frame, (int(sx * w), int(sy * h)), 6, (0, 255, 0), -1)
        cv2.circle(frame, (cx, cy), 6, (0, 0, 255), 2)
        cv2.putText(frame, f"dx={packet['dx']:+.2f} dy={packet['dy']:+.2f}",
                    (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        cv2.putText(frame, f"eyeA={ear_a:.2f}/{n_ear_a:.2f} eyeB={ear_b:.2f}/{n_ear_b:.2f}",
                    (10, 55), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (200, 200, 200), 1)
        cv2.putText(frame, f"mouth={mar:.2f} (open > {MOUTH_OPEN})",
                    (10, 78), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (200, 200, 200), 1)
        if packet["aim"]:
            cv2.putText(frame, "AIM", (10, 115), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 200, 255), 3)
        if packet["shoot"]:
            cv2.putText(frame, "SHOOT", (120, 115), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 255), 3)
    else:
        aim_count = shoot_count = 0
        cv2.putText(frame, "NO FACE", (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)

    sock.sendto(json.dumps(packet).encode(), UDP_ADDR)

    cv2.putText(frame, "C = calibrate (eyes open, mouth closed)   Q = quit",
                (10, h - 15), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
    cv2.imshow("Head Tracker", frame)

    key = cv2.waitKey(1) & 0xFF
    if key == ord("q"):
        break
    if key == ord("c") and sx is not None and result.multi_face_landmarks:
        neutral = (sx, sy, face_w, ear_a, ear_b)

cap.release()
cv2.destroyAllWindows()