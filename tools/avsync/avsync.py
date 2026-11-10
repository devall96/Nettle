"""Measure how far a TV's sound is ahead of / behind its picture.

1. make     Writes a test clip: a black screen that flashes white once a period, with a
            short 1 kHz beep starting on exactly the same instant (frame-aligned, so the
            file itself has zero offset). Put it in a Jellyfin library and play it in Nettle.
2. Film the TV with a phone (sound on, as close to where you sit as you like, 20 s or more;
   slow-motion 120/240 fps gives the most precise result).
3. analyze  Finds every flash and every beep in the recording, pairs them up and prints the
            offset, plus the Nettle "Audio timing" value that cancels it.

Needs ffmpeg / ffprobe on PATH; no Python packages. Positive offset = sound late.

    python tools/avsync/avsync.py make sync-23976.mkv --fps 23.976
    python tools/avsync/avsync.py make sync-23976-ac3.mkv --fps 23.976 --audio ac3   (passthrough)
    python tools/avsync/avsync.py analyze phone-recording.mp4
    python tools/avsync/avsync.py selftest
"""
import argparse
import array
import math
import os
import re
import shutil
import statistics
import subprocess
import sys
import tempfile

TONE_HZ = 1000.0
RATE = 48000


def need(tool):
    path = shutil.which(tool)
    if not path:
        sys.exit(f"{tool} not found on PATH (winget install Gyan.FFmpeg)")
    return path


def fps_fraction(fps):
    """23.976 -> 24000/1001, 29.97 -> 30000/1001, 59.94 -> 60000/1001, else whole numbers."""
    for nominal in (24, 30, 60, 120):
        if abs(fps - nominal * 1000 / 1001) < 0.01:
            return nominal * 1000, 1001
    return round(fps), 1


# ---------------------------------------------------------------- make

def make(out, fps, seconds, audio, offset_ms=0.0, width=1920, height=1080, gentle=False):
    """offset_ms != 0 deliberately shifts the beeps (+ = late); used by selftest."""
    ffmpeg = need("ffmpeg")
    num, den = fps_fraction(fps)
    frames_per_event = round(num / den)          # one event per ~second, on a whole frame
    period = frames_per_event * den / num        # e.g. 24 frames @ 23.976 = 1.001 s
    flash_frames = 2
    beep = 0.05
    shift = offset_ms / 1000.0
    # Beeps start at k * period (+ shift): the same instants as frames n = k * frames_per_event.
    tone = (f"sin(2*PI*{TONE_HZ}*t)*0.7*lt(mod(t-{shift}+{period * 10},{period}),{beep})"
            f"*gte(t,{max(shift, 0)})")
    enable = f"enable='lt(mod(n,{frames_per_event}),{flash_frames})'"
    # gentle: a mid-grey square, a quarter of the width, in the middle (photosensitivity-friendly; the apps' check).
    box = (f"drawbox=x=iw*3/8:y=ih*3/8:w=iw/4:h=ih/4:color=0x8C8C8C:t=fill:{enable}" if gentle
           else f"drawbox=x=0:y=0:w=iw:h=ih:color=white:t=fill:{enable}")
    video = f"color=c=black:s={width}x{height}:r={num}/{den}:d={seconds},{box},format=yuv420p"
    acodec = {"aac": ["-c:a", "aac", "-b:a", "192k", "-ac", "2"],
              "ac3": ["-c:a", "ac3", "-b:a", "448k", "-ac", "6"],
              "eac3": ["-c:a", "eac3", "-b:a", "640k", "-ac", "6"],
              "pcm": ["-c:a", "pcm_s16le", "-ac", "2"]}[audio]
    cmd = [ffmpeg, "-y", "-v", "error",
           "-f", "lavfi", "-i", video,
           "-f", "lavfi", "-i", f"aevalsrc='{tone}':s={RATE}:d={seconds}",
           "-c:v", "libx264", "-preset", "fast", "-crf", "18", "-g", str(frames_per_event),
           *acodec, "-shortest", out]
    subprocess.run(cmd, check=True)
    print(f"wrote {out}: {num}/{den} fps, a flash + beep every {period:.3f} s for {seconds} s, audio {audio}")


# ---------------------------------------------------------------- analyze

def video_brightness(path):
    """(pts seconds, mean luma) for every frame, from ffmpeg's showinfo."""
    ffmpeg = need("ffmpeg")
    p = subprocess.run([ffmpeg, "-v", "info", "-nostats", "-i", path, "-an",
                        "-vf", "scale=64:36,format=gray,showinfo", "-f", "null", "-"],
                       capture_output=True, text=True, errors="replace")
    out = []
    for line in p.stderr.splitlines():
        if "Parsed_showinfo" not in line or "pts_time:" not in line:
            continue
        t = re.search(r"pts_time:\s*([-\d.e]+)", line)
        m = re.search(r"mean:\[\s*([\d.]+)", line)
        if t and m:
            out.append((float(t.group(1)), float(m.group(1))))
    if not out:
        sys.exit("no video frames found:\n" + p.stderr[-2000:])
    return out


def audio_samples(path):
    ffmpeg = need("ffmpeg")
    p = subprocess.run([ffmpeg, "-v", "error", "-i", path, "-vn", "-ac", "1", "-ar", str(RATE),
                        "-f", "s16le", "-"], capture_output=True, check=True)
    a = array.array("h")
    a.frombytes(p.stdout[: len(p.stdout) // 2 * 2])
    if sys.byteorder == "big":
        a.byteswap()
    return a


def audio_start(path):
    """Audio stream start relative to video, so both sides use the container's timeline."""
    ffprobe = need("ffprobe")
    def start(sel):
        p = subprocess.run([ffprobe, "-v", "error", "-select_streams", sel, "-show_entries",
                            "stream=start_time", "-of", "csv=p=0", path], capture_output=True, text=True)
        try:
            return float(p.stdout.strip().splitlines()[0])
        except (ValueError, IndexError):
            return 0.0
    return start("a:0"), start("v:0")


def flash_onsets(frames):
    lum = [m for _, m in frames]
    lo, hi = percentile(lum, 10), percentile(lum, 99)
    if hi - lo < 10:
        sys.exit("no flashes found: is the TV in the shot and the clip playing?")
    onsets = []
    for i in range(1, len(frames)):
        t, m = frames[i]
        f = (m - lo) / (hi - lo)
        prev = (frames[i - 1][1] - lo) / (hi - lo)
        if prev < 0.25 <= f:
            # A frame at pts t covers [t, t + dt). A camera often catches the flash part-way through
            # one: a frame only `frac` as bright as the next saw it for the last `frac` of its time.
            dt = t - frames[i - 1][0]
            frac = 1.0
            if i + 1 < len(frames):
                nxt = (frames[i + 1][1] - lo) / (hi - lo)
                frac = min(1.0, max(f, 0.0) / max(nxt, f, 1e-6))
            onsets.append(t + (1 - frac) * dt)
    return onsets


def beep_onsets(samples, start):
    """1 kHz envelope (I/Q mix + 4 ms moving average), onset = crossing half the beep level."""
    n = len(samples)
    w = 2 * math.pi * TONE_HZ / RATE
    win = int(RATE * 0.004)
    i_sum = [0.0] * (n + 1)
    q_sum = [0.0] * (n + 1)
    si = sq = 0.0
    for k in range(n):
        s = samples[k]
        si += s * math.cos(w * k)
        sq += s * math.sin(w * k)
        i_sum[k + 1] = si
        q_sum[k + 1] = sq
    step = RATE // 2000  # 0.5 ms resolution
    env = []
    for k in range(win, n + 1, step):
        i = i_sum[k] - i_sum[k - win]
        q = q_sum[k] - q_sum[k - win]
        env.append((k - win / 2, math.hypot(i, q)))
    vals = [e for _, e in env]
    lo, hi = percentile(vals, 20), percentile(vals, 99.5)
    if hi < lo * 4:
        sys.exit("no beeps found: was the sound on, and the phone close enough?")
    thr = lo + (hi - lo) * 0.5
    onsets, armed = [], True
    for j in range(1, len(env)):
        (k0, e0), (k1, e1) = env[j - 1], env[j]
        # A room echo or the TV's audio processing can dip a beep mid-way: a second crossing within
        # 0.3 s is the same beep, not a new one (beeps are ~1 s apart).
        if armed and e0 < thr <= e1 and (not onsets or start + k1 / RATE - onsets[-1] > 0.3):
            k = k0 + (k1 - k0) * (thr - e0) / (e1 - e0)
            onsets.append(start + k / RATE)
            armed = False
        elif not armed and e1 < lo + (hi - lo) * 0.2:
            armed = True
    # Each envelope point is placed at its window's centre, where a step reaches half level: no correction needed.
    return onsets


def percentile(xs, p):
    s = sorted(xs)
    return s[min(len(s) - 1, max(0, int(round(p / 100 * (len(s) - 1)))))]


def analyze(path, quiet=False, bias_ms=0.0):
    frames = video_brightness(path)
    a_start, v_start = audio_start(path)
    flashes = flash_onsets(frames)
    beeps = beep_onsets(audio_samples(path), a_start)
    diffs = []
    for f in flashes:
        near = min(beeps, key=lambda b: abs(b - f), default=None)
        if near is not None and abs(near - f) < 0.4:
            diffs.append((near - f) * 1000 - bias_ms)
    if len(diffs) < 3:
        sys.exit(f"only {len(diffs)} flash/beep pairs matched ({len(flashes)} flashes, {len(beeps)} beeps)")
    med = statistics.median(diffs)
    q = statistics.quantiles(diffs, n=4) if len(diffs) >= 4 else [min(diffs), med, max(diffs)]
    fps = (len(frames) - 1) / (frames[-1][0] - frames[0][0])
    if not quiet:
        print(f"recording: {fps:.1f} fps camera, {len(flashes)} flashes, {len(beeps)} beeps, {len(diffs)} pairs")
        print("per event (ms, + = sound late): " + " ".join(f"{d:+.0f}" for d in diffs))
        print(f"offset: {med:+.1f} ms (middle half {q[0]:+.1f} to {q[2]:+.1f})")
        if abs(med) < 15:
            print("in sync (within what people can notice)")
        else:
            side = "AHEAD of" if med < 0 else "BEHIND"
            fix = int(round(-med / 10) * 10)
            print(f"the sound is {abs(med):.0f} ms {side} the picture -> Nettle Audio timing {fix:+d} ms "
                  f"(add it to the value that was set while recording)")
    return med


def claps(path, out_dir):
    """The phone's own A/V offset: claps make sound and picture at the same instant. Finds each clap in
    the sound and writes a strip of the frames around it, labelled with their time from the sound
    (+ = after it), to pick the frame where the hands meet. That frame's label is the phone's bias:
    a negative label means the picture is recorded earlier than the sound."""
    ffmpeg = need("ffmpeg")
    a = audio_samples(path)
    a_start, _ = audio_start(path)
    step = RATE // 1000  # 1 ms energy steps
    env = [sum(abs(x) for x in a[i:i + step]) / step for i in range(0, len(a) - step, step)]
    noise = percentile(env, 50)
    peak = percentile(env, 99.9)
    hits, last = [], -1.0
    for i, e in enumerate(env):
        t = a_start + i / 1000
        if e > noise + (peak - noise) * 0.3 and t - last > 0.4:
            # the onset: walk back to where the level first rose clearly above the noise
            j = i
            while j > 0 and env[j - 1] > noise * 3:
                j -= 1
            hits.append(a_start + j / 1000)
            last = t
    os.makedirs(out_dir, exist_ok=True)
    for n, t in enumerate(hits):
        start = max(0.0, t - 0.1)
        sheet = os.path.join(out_dir, f"clap{n + 1}.jpg")
        label = f"drawtext=text='%{{eif\\:(t-{t - start:.4f})*1000\\:d}} ms':x=8:y=8:fontsize=28:fontcolor=yellow:box=1:boxcolor=black"
        subprocess.run([ffmpeg, "-v", "error", "-y", "-ss", f"{start:.4f}", "-i", path, "-t", "0.2",
                        "-vf", f"setpts=PTS-STARTPTS,scale=480:-1,{label},tile=6x4", "-frames:v", "1", sheet], check=True)
        print(f"clap {n + 1}: sound at {t:.3f} s -> {sheet}")
    if not hits:
        print("no claps found")


def selftest():
    """Builds clips with known offsets and checks analyze() finds them (needs ffmpeg only)."""
    tmp = tempfile.mkdtemp(prefix="avsync-")
    ok = True
    for fps, off in ((23.976, 0), (23.976, -40), (25, 60), (59.94, -20)):
        clip = os.path.join(tmp, f"t-{fps}-{off}.mkv")
        make(clip, fps, 12, "aac", offset_ms=off, width=320, height=180)
        got = analyze(clip, quiet=True)
        good = abs(got - off) <= 3
        ok &= good
        print(f"{fps:>7} fps, beeps shifted {off:+4d} ms -> measured {got:+6.1f} ms  {'ok' if good else 'FAIL'}")
    shutil.rmtree(tmp, ignore_errors=True)
    sys.exit(0 if ok else 1)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    m = sub.add_parser("make", help="write a flash + beep test clip")
    m.add_argument("out")
    m.add_argument("--fps", type=float, default=23.976)
    m.add_argument("--seconds", type=int, default=120)
    m.add_argument("--audio", choices=["aac", "ac3", "eac3", "pcm"], default="aac")
    m.add_argument("--gentle", action="store_true",
                   help="photosensitivity-friendly: a mid-grey square in the middle instead of a full white screen "
                        "(the clip bundled in the apps' Lip sync check; too faint for `analyze`)")
    a = sub.add_parser("analyze", help="measure the offset in a phone recording of the TV")
    a.add_argument("recording")
    a.add_argument("--phone-bias", type=float, default=0.0,
                   help="the phone's own offset from `claps` (ms, + = it records sound late); taken off the result")
    c = sub.add_parser("claps", help="measure the phone's own A/V offset from a recording of claps")
    c.add_argument("recording")
    c.add_argument("--out", default="claps")
    sub.add_parser("selftest", help="check the tool against clips with known offsets")
    args = ap.parse_args()
    if args.cmd == "make":
        make(args.out, args.fps, args.seconds, args.audio, gentle=args.gentle)
    elif args.cmd == "analyze":
        analyze(args.recording, bias_ms=args.phone_bias)
    elif args.cmd == "claps":
        claps(args.recording, args.out)
    else:
        selftest()


if __name__ == "__main__":
    main()
