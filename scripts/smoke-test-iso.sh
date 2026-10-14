#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out_dir="$repo_root/out"
boot_timeout="${DAISYOS_QEMU_BOOT_TIMEOUT_SECONDS:-360}"
render_timeout="${DAISYOS_QEMU_RENDER_TIMEOUT_SECONDS:-}"
stability_seconds="${DAISYOS_QEMU_STABILITY_SECONDS:-60}"
firmware="bios"
exercise_recovery=false
keep_failed_vm="${DAISYOS_QEMU_KEEP_FAILED_VM:-0}"
smoke_passed=false

usage() {
  cat <<'EOF'
Usage: scripts/smoke-test-iso.sh [--firmware bios|uefi] [--exercise-recovery] [ISO]

Boots a disposable live ISO, requires graphical.target and a visible DaisyOS
frame, then watches briefly for a supervised-shell crash. Recovery mode also
terminates the real shell once and requires the supervisor to restart it.
EOF
}

while (( $# > 0 )); do
  case "$1" in
    --firmware)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      firmware="$2"
      shift 2
      ;;
    --exercise-recovery)
      exercise_recovery=true
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    --*)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
    *)
      if [[ -n "${iso_path:-}" ]]; then
        echo "Only one ISO path may be supplied." >&2
        exit 2
      fi
      iso_path="$1"
      shift
      ;;
  esac
done

if [[ "$firmware" != "bios" && "$firmware" != "uefi" ]]; then
  echo "Firmware must be 'bios' or 'uefi'." >&2
  exit 2
fi

artifact_dir="${DAISYOS_QEMU_ARTIFACT_DIR:-$repo_root/artifacts/iso-smoke}"
if [[ -z "${DAISYOS_QEMU_ARTIFACT_DIR:-}" && "$firmware" == "uefi" ]]; then
  artifact_dir="$repo_root/artifacts/iso-smoke-uefi"
fi

for command in qemu-system-x86_64 socat magick base64; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "$command is required for the headless ISO smoke test."
    exit 1
  fi
done

iso_path="${iso_path:-}"
if [[ -z "$iso_path" ]]; then
  iso_path="$(find "$out_dir" -maxdepth 1 -type f -name '*.iso' -printf '%T@ %p\n' 2>/dev/null \
    | sort -nr | sed -n '1s/^[^ ]* //p')"
fi
if [[ -z "$iso_path" || ! -f "$iso_path" ]]; then
  echo "No ISO was found. Build one with sudo scripts/build-iso.sh first."
  exit 1
fi

rm -rf "$artifact_dir"
mkdir -p "$artifact_dir"
serial_log="$artifact_dir/serial.log"
monitor_socket="$artifact_dir/monitor.sock"
qga_socket="$artifact_dir/qga.sock"
pid_file="$artifact_dir/qemu.pid"
framebuffer="$artifact_dir/framebuffer.ppm"
framebuffer_png="$artifact_dir/framebuffer.png"
payload_override="${DAISYOS_QEMU_PAYLOAD_OVERRIDE:-}"
skip_onboarding="${DAISYOS_QEMU_SKIP_ONBOARDING:-0}"
payload_override_args=()
if [[ -n "$payload_override" ]]; then
  payload_override="$(realpath "$payload_override")"
  if [[ ! -x "$payload_override/DaisyOS.Shell" ]]; then
    echo "The diagnostic payload override is not a published DaisyOS shell." >&2
    exit 1
  fi
  payload_override_args=(-virtfs "local,path=$payload_override,mount_tag=daisy_payload,security_model=none,readonly=on")
fi

machine_args=(-machine q35 -accel "tcg,thread=multi" -cpu qemu64)
acceleration="software emulation"
qga_timeout=15
if [[ -r /dev/kvm && -w /dev/kvm ]]; then
  machine_args=(-machine q35 -accel kvm -cpu host)
  acceleration="KVM"
  qga_timeout=3
fi
if [[ -z "$render_timeout" ]]; then
  if [[ "$acceleration" == "KVM" ]]; then
    render_timeout=240
  else
    render_timeout=600
  fi
fi

firmware_args=()
video_args=(-device virtio-vga)
if [[ "$firmware" == "uefi" ]]; then
  ovmf_code="${DAISYOS_OVMF_CODE:-/usr/share/edk2/x64/OVMF_CODE.4m.fd}"
  ovmf_vars_template="${DAISYOS_OVMF_VARS:-/usr/share/edk2/x64/OVMF_VARS.4m.fd}"
  if [[ ! -f "$ovmf_code" || ! -f "$ovmf_vars_template" ]]; then
    echo "UEFI smoke testing requires OVMF firmware files." >&2
    exit 1
  fi
  ovmf_vars="$artifact_dir/OVMF_VARS.4m.fd"
  cp "$ovmf_vars_template" "$ovmf_vars"
  firmware_args=(
    -drive "if=pflash,format=raw,readonly=on,file=$ovmf_code"
    -drive "if=pflash,format=raw,file=$ovmf_vars"
  )
  # OVMF plus virtio-vga under TCG corrupts the native software-rendering path.
  # Standard VGA is the stable UEFI emulation path and matches physical GOP use.
  video_args=(-vga std)
fi

# shellcheck disable=SC2329 # Invoked indirectly by the EXIT trap.
cleanup() {
  if [[ "$keep_failed_vm" == "1" && "$smoke_passed" != true ]]; then
    echo "Keeping failed VM for diagnostics: $pid_file" >&2
    return
  fi
  if [[ -f "$pid_file" ]]; then
    qemu_pid="$(cat "$pid_file")"
    kill "$qemu_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

qga_request() {
  local request="$1"
  { printf '%s\n' "$request"; sleep 1; } \
    | timeout "$qga_timeout" socat - "UNIX-CONNECT:$qga_socket" 2>/dev/null \
    | tail -n 1
}

prepare_payload_override() {
  [[ -n "$payload_override" ]] || return 0
  local deadline=$((SECONDS + boot_timeout))
  local response=""
  local command='set -eu; ! pgrep -u daisyos -f /opt/DaisyOS/shell/DaisyOS.Shell >/dev/null; mkdir -p /run/daisy-payload; mount -t 9p -o trans=virtio,version=9p2000.L,ro daisy_payload /run/daisy-payload; mount --bind /run/daisy-payload /opt/DaisyOS/shell; test -x /opt/DaisyOS/shell/DaisyOS.Shell'
  local onboarding_settings
  local pid status

  if [[ "$skip_onboarding" == "1" ]]; then
    onboarding_settings="$(printf '%s' '{"OnboardingPreferences":{"CompletedVersion":1}}' | base64 -w0)"
    command="$command; install -d -o daisyos -g users /home/daisyos/.config/DaisyOS; printf %s $onboarding_settings | base64 -d > /home/daisyos/.config/DaisyOS/settings.json; chown daisyos:users /home/daisyos/.config/DaisyOS/settings.json"
  fi

  while (( SECONDS < deadline )); do
    response="$(qga_request '{"execute":"guest-sync","arguments":{"id":41}}' || true)"
    grep -Fq '"return": 41' <<<"$response" && break
    sleep 1
  done
  if ! grep -Fq '"return": 41' <<<"$response"; then
    echo "The diagnostic payload channel did not become ready." >&2
    return 1
  fi

  response="$(qga_request "{\"execute\":\"guest-exec\",\"arguments\":{\"path\":\"/bin/sh\",\"arg\":[\"-lc\",\"$command\"],\"capture-output\":true}}")"
  pid="$(sed -n 's/.*"pid": \?\([0-9]*\).*/\1/p' <<<"$response")"
  [[ -n "$pid" ]] || return 1
  deadline=$((SECONDS + 30))
  while (( SECONDS < deadline )); do
    status="$(qga_request "{\"execute\":\"guest-exec-status\",\"arguments\":{\"pid\":$pid}}" || true)"
    if grep -Fq '"exited": true' <<<"$status"; then
      if grep -Eq '"exitcode": 0([,}])' <<<"$status"; then
        echo "Mounted diagnostic production payload before session startup."
        return 0
      fi
      echo "Could not mount the diagnostic production payload before session startup." >&2
      return 1
    fi
    sleep 1
  done
  return 1
}

verify_live_runtime() {
  local allowed_crashes="${1:-0}"
  local deadline=$((SECONDS + 60))
  local response=""
  local command
  local pid
  local status
  local output
  local firmware_check='test ! -d /sys/firmware/efi'

  if [[ "$firmware" == "uefi" ]]; then
    firmware_check='test -d /sys/firmware/efi'
  fi

  while (( SECONDS < deadline )); do
    response="$(qga_request '{"execute":"guest-sync","arguments":{"id":29}}' || true)"
    if grep -Fq '"return": 29' <<<"$response"; then
      break
    fi
    sleep 2
  done
  if ! grep -Fq '"return": 29' <<<"$response"; then
    echo "The QEMU runtime diagnostics channel did not become ready." >&2
    return 1
  fi

  command="set -eu; fail() { echo missing=\$1 >&2; exit 1; }; wait_process() { name=\$1; deadline=60; while test \$deadline -gt 0; do pgrep -u daisyos -x \$name >/dev/null && return 0; deadline=\$((deadline - 1)); sleep 1; done; fail \$name; }; id -u daisyos >/dev/null || fail live-user; id -nG daisyos | grep -qw tty || fail tty-group; systemctl is-active --quiet NetworkManager || fail NetworkManager; wait_process kwin_wayland; wait_process Xwayland; shell_pid=\$(pgrep -u daisyos -o -f /opt/DaisyOS/shell/DaisyOS.Shell); test -n \$shell_pid || fail shell; wait_process pipewire; wait_process pipewire-pulse; wait_process wireplumber; test -x /opt/DaisyOS/shell/DaisyOS.Shell || fail shell-payload; crash_count=\$(grep -c 'stopped unexpectedly' /home/daisyos/.local/state/DaisyOS/session.log 2>/dev/null || true); test \$crash_count -eq $allowed_crashes || fail unexpected-shell-stop; $firmware_check || fail firmware-mode; echo shell_pid=\$shell_pid; echo crash_count=\$crash_count; echo compositor=kwin_wayland; echo compatibility=Xwayland"
  response="$(qga_request "{\"execute\":\"guest-exec\",\"arguments\":{\"path\":\"/bin/sh\",\"arg\":[\"-lc\",\"$command\"],\"capture-output\":true}}")"
  pid="$(sed -n 's/.*"pid": \?\([0-9]*\).*/\1/p' <<<"$response")"
  if [[ -z "$pid" ]]; then
    echo "Could not start live runtime verification." >&2
    return 1
  fi

  deadline=$((SECONDS + 30))
  while (( SECONDS < deadline )); do
    status="$(qga_request "{\"execute\":\"guest-exec-status\",\"arguments\":{\"pid\":$pid}}" || true)"
    if grep -Fq '"exited": true' <<<"$status"; then
      if grep -Eq '"exitcode": 0([,}])' <<<"$status"; then
        encoded="$(sed -n 's/.*"out-data": "\([^"]*\)".*/\1/p' <<<"$status")"
        output="$(printf '%s' "$encoded" | base64 -d 2>/dev/null || true)"
        printf 'firmware=%s\nnetworkmanager=active\nxorg=active\nshell=active\npipewire=active\npipewire_pulse=active\nwireplumber=active\n' \
          "$firmware" > "$artifact_dir/runtime.txt"
        printf '%s\n' "$output" >> "$artifact_dir/runtime.txt"
        return 0
      fi
      output="$(sed -n 's/.*"err-data": "\([^"]*\)".*/\1/p' <<<"$status" | base64 -d 2>/dev/null || true)"
      echo "The live runtime service check failed. ${output:-Inspect the serial log for details.}" >&2
      return 1
    fi
    sleep 1
  done

  echo "Timed out while checking live runtime services." >&2
  return 1
}

capture_guest_diagnostics() {
  local command='echo ==processes==; ps -ef; echo ==session-log==; cat /home/daisyos/.local/state/DaisyOS/session.log 2>&1 || true; echo ==shell-log==; cat /home/daisyos/.local/state/DaisyOS/shell.log 2>&1 || true; echo ==tty1==; systemctl --no-pager --full status getty@tty1.service || true; echo ==tty1-journal==; journalctl -b --no-pager -u getty@tty1.service || true; echo ==user-files==; ls -la /home/daisyos /home/daisyos/.local/state/DaisyOS 2>&1 || true; echo ==xorg-log==; tail -n 120 /home/daisyos/.local/share/xorg/Xorg.0.log 2>&1 || true'
  local response
  local pid
  local status
  local encoded
  local deadline

  response="$(qga_request "{\"execute\":\"guest-exec\",\"arguments\":{\"path\":\"/bin/sh\",\"arg\":[\"-lc\",\"$command\"],\"capture-output\":true}}" || true)"
  pid="$(sed -n 's/.*"pid": \?\([0-9]*\).*/\1/p' <<<"$response")"
  [[ -n "$pid" ]] || return 0

  deadline=$((SECONDS + 15))
  while (( SECONDS < deadline )); do
    status="$(qga_request "{\"execute\":\"guest-exec-status\",\"arguments\":{\"pid\":$pid}}" || true)"
    if grep -Fq '"exited": true' <<<"$status"; then
      encoded="$(sed -n 's/.*"out-data": "\([^"]*\)".*/\1/p' <<<"$status")"
      if [[ -n "$encoded" ]]; then
        printf '%s' "$encoded" | base64 -d > "$artifact_dir/guest-diagnostics.txt" 2>/dev/null || true
        echo "Guest diagnostics: $artifact_dir/guest-diagnostics.txt" >&2
      fi
      return 0
    fi
    sleep 1
  done
}

exercise_shell_recovery() {
  # shellcheck disable=SC2016 # Expanded by /bin/sh inside the disposable guest.
  local command='old=$(pgrep -u daisyos -o -f /opt/DaisyOS/shell/DaisyOS.Shell); test -n $old; kill -TERM $old; deadline=90; while test $deadline -gt 0; do new=$(pgrep -u daisyos -n -f /opt/DaisyOS/shell/DaisyOS.Shell || true); if test -n $new && test $new != $old; then evidence=30; while test $evidence -gt 0; do log=/home/daisyos/.local/state/DaisyOS/session.log; stopped=$(grep -c stopped.unexpectedly $log || true); diagnostics=$(grep -c Crash.diagnostics $log || true); if test $stopped -eq 1 && test $diagnostics -eq 1 && grep -q status.143 $log; then echo old_pid=$old; echo new_pid=$new; cat $log; exit 0; fi; evidence=$((evidence - 1)); sleep 1; done; exit 1; fi; deadline=$((deadline - 1)); sleep 1; done; exit 1'
  local response
  local pid
  local status
  local encoded
  local deadline

  response="$(qga_request "{\"execute\":\"guest-exec\",\"arguments\":{\"path\":\"/bin/sh\",\"arg\":[\"-lc\",\"$command\"],\"capture-output\":true}}")"
  pid="$(sed -n 's/.*"pid": \?\([0-9]*\).*/\1/p' <<<"$response")"
  if [[ -z "$pid" ]]; then
    echo "Could not start the shell recovery exercise." >&2
    return 1
  fi

  deadline=$((SECONDS + 120))
  while (( SECONDS < deadline )); do
    status="$(qga_request "{\"execute\":\"guest-exec-status\",\"arguments\":{\"pid\":$pid}}" || true)"
    if grep -Fq '"exited": true' <<<"$status"; then
      if grep -Eq '"exitcode": 0([,}])' <<<"$status"; then
        encoded="$(sed -n 's/.*"out-data": "\([^"]*\)".*/\1/p' <<<"$status")"
        printf '%s' "$encoded" | base64 -d > "$artifact_dir/recovery.txt" 2>/dev/null || true
        printf 'result=passed\n' >> "$artifact_dir/recovery.txt"
        return 0
      fi
      echo "The supervised shell did not recover in the disposable VM." >&2
      return 1
    fi
    sleep 1
  done

  echo "Timed out while waiting for supervised shell recovery." >&2
  return 1
}

printf 'Booting %s through %s with %s for headless smoke testing.\n' \
  "$iso_path" "${firmware^^}" "$acceleration"
qemu-system-x86_64 \
  "${machine_args[@]}" \
  "${firmware_args[@]}" \
  "${payload_override_args[@]}" \
  -name "DaisyOS-ISO-Smoke-${firmware^^}" \
  -m 4096 \
  -smp 4 \
  "${video_args[@]}" \
  -cdrom "$iso_path" \
  -boot once=d \
  -chardev "socket,path=$qga_socket,server=on,wait=off,id=qga0" \
  -device virtio-serial \
  -device "virtserialport,chardev=qga0,name=org.qemu.guest_agent.0" \
  -display none \
  -serial "file:$serial_log" \
  -monitor "unix:$monitor_socket,server=on,wait=off" \
  -no-reboot \
  -daemonize \
  -pidfile "$pid_file"

if ! prepare_payload_override; then
  exit 1
fi

deadline=$((SECONDS + boot_timeout))
while (( SECONDS < deadline )); do
  if grep -q 'Reached target.*Graphical Interface' "$serial_log" 2>/dev/null; then
    break
  fi
  if [[ ! -f "$pid_file" ]] || ! kill -0 "$(cat "$pid_file")" 2>/dev/null; then
    echo "QEMU stopped before the graphical live session was ready."
    tail -n 60 "$serial_log" || true
    exit 1
  fi
  sleep 2
done

if ! grep -q 'Reached target.*Graphical Interface' "$serial_log"; then
  echo "The ISO did not reach the graphical live session within ${boot_timeout}s."
  tail -n 60 "$serial_log" || true
  exit 1
fi
if grep -Eqi 'kernel panic|not syncing: fatal' "$serial_log"; then
  echo "The live kernel reported a fatal boot failure."
  exit 1
fi

shell_crash_pattern='DAISYOS_SESSION:.*stopped unexpectedly|DAISYOS_SESSION:.*Crash diagnostics:|free\(\): invalid pointer'

frame_has_visible_desktop() {
  local image="$1"
  local colors nonblack mean
  colors="$(magick "$image" -format '%k' info:)"
  nonblack="$(magick "$image" -colorspace gray -threshold 2% -format '%[fx:mean]' info:)"
  mean="$(magick "$image" -colorspace RGB -format '%[fx:mean]' info:)"
  color_count="$colors"
  frame_nonblack="$nonblack"
  frame_mean="$mean"

  [[ "$colors" =~ ^[0-9]+$ ]] || return 1
  (( colors > 256 )) || return 1
  awk -v nonblack="$nonblack" -v mean="$mean" \
    'BEGIN { exit !(nonblack >= 0.20 && mean >= 0.01) }'
}

deadline=$((SECONDS + render_timeout))
color_count=0
frame_nonblack=0
frame_mean=0
while (( SECONDS < deadline )); do
  if grep -Eqi "$shell_crash_pattern" "$serial_log" 2>/dev/null; then
    echo "The supervised DaisyOS shell crashed before producing a stable frame."
    grep -Ei 'DAISYOS_SESSION:|free\(\): invalid pointer' "$serial_log" | tail -n 20 || true
    exit 1
  fi
  printf 'screendump %s\n' "$framebuffer" | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null
  if frame_has_visible_desktop "$framebuffer"; then
    magick "$framebuffer" "$framebuffer_png"
    break
  fi
  sleep 5
done

if ! frame_has_visible_desktop "$framebuffer"; then
  echo "The live session reached graphical.target but did not render a visible DaisyOS frame within ${render_timeout}s."
  capture_guest_diagnostics
  exit 1
fi

if ! verify_live_runtime 0; then
  capture_guest_diagnostics
  exit 1
fi
stable_shell_pid="$(sed -n 's/^shell_pid=//p' "$artifact_dir/runtime.txt" | tail -n 1)"
if [[ ! "$stable_shell_pid" =~ ^[0-9]+$ ]]; then
  echo "The live runtime check did not identify the shell process." >&2
  exit 1
fi

if [[ "$exercise_recovery" == true ]]; then
  if ! exercise_shell_recovery; then
    capture_guest_diagnostics
    exit 1
  fi
  stable_shell_pid="$(sed -n 's/^new_pid=//p' "$artifact_dir/recovery.txt" | tail -n 1)"
  if [[ ! "$stable_shell_pid" =~ ^[0-9]+$ ]]; then
    echo "The recovery exercise did not identify the replacement shell process." >&2
    exit 1
  fi
fi

deadline=$((SECONDS + stability_seconds))
while (( SECONDS < deadline )); do
  if [[ "$exercise_recovery" != true ]] \
    && grep -Eqi "$shell_crash_pattern" "$serial_log" 2>/dev/null; then
    echo "The supervised DaisyOS shell crashed during the stability window."
    grep -Ei 'DAISYOS_SESSION:|free\(\): invalid pointer' "$serial_log" | tail -n 20 || true
    exit 1
  fi
  if [[ ! -f "$pid_file" ]] || ! kill -0 "$(cat "$pid_file")" 2>/dev/null; then
    echo "QEMU stopped during the framebuffer stability window."
    exit 1
  fi
  sleep 1
done

expected_crashes=0
if [[ "$exercise_recovery" == true ]]; then
  expected_crashes=1
fi
if ! verify_live_runtime "$expected_crashes"; then
  exit 1
fi
final_shell_pid="$(sed -n 's/^shell_pid=//p' "$artifact_dir/runtime.txt" | tail -n 1)"
if [[ "$final_shell_pid" != "$stable_shell_pid" ]]; then
  echo "The shell process changed during the post-render stability window." >&2
  exit 1
fi

# A colorful startup fragment is not sufficient evidence. Re-check the full
# framebuffer after recovery and the stability window so a black compositor
# surface or partially rendered wallpaper cannot pass the release gate.
printf 'screendump %s\n' "$framebuffer" | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null
if ! frame_has_visible_desktop "$framebuffer"; then
  echo "The DaisyOS desktop was no longer fully visible after the stability window." >&2
  printf 'Frame evidence: colors=%s nonblack=%s mean=%s\n' \
    "$color_count" "$frame_nonblack" "$frame_mean" >&2
  capture_guest_diagnostics
  exit 1
fi
magick "$framebuffer" "$framebuffer_png"

smoke_passed=true
printf 'Visible DaisyOS %s framebuffer confirmed with %s colors, %s non-black coverage, and %ss stability.\n' \
  "${firmware^^}" "$color_count" "$frame_nonblack" "$stability_seconds"
printf 'Evidence: %s\n' "$framebuffer_png"
if [[ "$exercise_recovery" == true ]]; then
  printf 'Recovery evidence: %s\n' "$artifact_dir/recovery.txt"
fi
