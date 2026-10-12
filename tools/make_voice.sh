#!/usr/bin/env bash
# Generates the robots' voice into ../Sounds/Voice*.wav with the Piper text-to-speech engine, then makes
# it flat, buzzy and lo-fi with tools/robot_voice.py, for the cabinet's talking robots.
#
# Needs a Piper binary and the en_GB-vctk-medium voice (VCTK corpus, CC BY 4.0), speaker 75 (VCTK p345).
# Override the paths with
# PIPER=/path/to/piper and VOICE=/path/to/model.onnx.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../Sounds"
piper="${PIPER:-$HOME/.local/share/piper/bin/piper}"
voice="${VOICE:-$HOME/.local/share/piper/voices/en_GB-vctk-medium.onnx}"
speaker="${SPEAKER:-75}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

say() {
  local name="$1" text="$2"
  echo "$text" | "$piper" --model "$voice" --speaker "$speaker" --length_scale 1.05 \
    --noise_scale 0.2 --noise_w 0.3 --output_file "$tmp/$name.wav" >/dev/null 2>&1
  python3 "$here/robot_voice.py" "$tmp/$name.wav" "$out/Voice$name.wav"
}

mkdir -p "$out"
say IntruderAlert   "Intruder alert! Intruder alert!"
say MustNotEscape   "The humanoid must not escape!"
say Chicken         "Chicken! Fight like a robot!"
say GotHumanoid     "Got the humanoid! Got the intruder!"
say KillHumanoid    "Kill the humanoid!"
say DestroyIntruder "Destroy the intruder!"
say ChargeAttack    "Charge! Attack!"
say ShootIt         "Shoot it! Shoot it!"
say GetHumanoid     "Get the humanoid!"
say CoinsDetected   "Coins detected in pocket!"
echo "Wrote voices to $(cd "$out" && pwd)"
