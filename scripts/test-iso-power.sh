#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out_dir="$repo_root/out"
boot_timeout="${DAISYOS_QEMU_BOOT_TIMEOUT_SECONDS:-360}"
render_timeout="${DAISYOS_QEMU_RENDER_TIMEOUT_SECONDS:-}"
power_timeout="${DAISYOS_QEMU_POWER_TIMEOUT_SECONDS:-}"
provision_power_wrapper="${DAISYOS_QEMU_PROVISION_POWER_WRAPPER:-0}"
requested_action="all"
active_pid_file=""
power_ready_evidence=""

# shellcheck disable=SC2329 # Invoked indirectly by the EXIT trap.
cleanup() {
  local pid
  if [[ -n "$active_pid_file" && -f "$active_pid_file" ]]; then
    pid="$(cat "$active_pid_file" 2>/dev/null || true)"
    if [[ "$pid" =~ ^[0-9]+$ ]]; then
      kill "$pid" 2>/dev/null || true
    fi
  fi
}
trap cleanup EXIT

usage() {
  cat <<'EOF'
Usage: scripts/test-iso-power.sh [--action reboot|shutdown|all] [ISO]

Exercises the real Restart and Shut down buttons inside separate disposable
BIOS VMs. No writable disk is attached.
EOF
}

while (( $# > 0 )); do
  case "$1" in
    --action)
      [[ $# -ge 2 ]] || { usage >&2; exit 2; }
      requested_action="$2"
      shift 2
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

case "$requested_action" in
  reboot) actions=(reboot) ;;
  shutdown) actions=(shutdown) ;;
  all) actions=(reboot shutdown) ;;
  *)
    echo "Action must be 'reboot', 'shutdown', or 'all'." >&2
    exit 2
    ;;
esac

for command in qemu-system-x86_64 socat magick base64; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "$command is required for the ISO power test." >&2
    exit 1
  fi
done

iso_path="${iso_path:-}"
if [[ -z "$iso_path" ]]; then
  iso_path="$(find "$out_dir" -maxdepth 1 -type f -name '*.iso' -printf '%T@ %p\n' 2>/dev/null \
    | sort -nr | sed -n '1s/^[^ ]* //p')"
fi

qga_request() {
  local socket="$1"
  local request="$2"
  { printf '%s\n' "$request"; sleep 1; } \
    | timeout "$qga_timeout" socat - "UNIX-CONNECT:$socket" 2>/dev/null \
    | tail -n 1
}

provision_completed_onboarding() {
  local socket="$1"
  local deadline=$((SECONDS + boot_timeout))
  local response=""
  local settings_json='{"OnboardingPreferences":{"CompletedVersion":1}}'
  local settings_base64
  local wrapper_base64=""
  local wrapper_command=""
  local command
  local pid
  local status
  local encoded
  local output

  while (( SECONDS < deadline )); do
    response="$(qga_request "$socket" '{"execute":"guest-sync","arguments":{"id":17}}' || true)"
    if grep -Fq '"return": 17' <<<"$response"; then
      break
    fi
    sleep 2
  done
  if ! grep -Fq '"return": 17' <<<"$response"; then
    echo "The conditional QEMU diagnostic channel did not become ready." >&2
    return 1
  fi

  settings_base64="$(printf '%s' "$settings_json" | base64 -w0)"
  if [[ "$provision_power_wrapper" == "1" ]]; then
    wrapper_base64="$(printf '%s\n' '#!/bin/sh' 'printf "DAISYOS_POWER_WRAPPER action=%s\\n" "$*" > /dev/ttyS0' 'exec sudo -n /usr/bin/systemctl.real "$@"' | base64 -w0)"
    wrapper_command="mv /usr/bin/systemctl /usr/bin/systemctl.real; printf %s $wrapper_base64 | base64 -d > /usr/bin/systemctl; chmod 755 /usr/bin/systemctl;"
  fi
  command="set -eu; until id -u daisyos >/dev/null 2>&1; do sleep 1; done; $wrapper_command install -d -o daisyos -g users /home/daisyos/.config/DaisyOS; printf %s $settings_base64 | base64 -d > /home/daisyos/.config/DaisyOS/settings.json; chown daisyos:users /home/daisyos/.config/DaisyOS/settings.json; deadline=$boot_timeout; shell_pid=; log=/home/daisyos/.local/state/DaisyOS/session.log; while test \$deadline -gt 0; do shell_pid=\$(pgrep -u daisyos -o -f /opt/DaisyOS/shell/DaisyOS.Shell || true); test -n \$shell_pid && test -f \$log && break; deadline=\$((deadline - 1)); sleep 1; done; test -n \$shell_pid; test -f \$log; crash_count=\$(grep -c stopped.unexpectedly \$log || true); test \$crash_count -eq 0; printf 'DAISYOS_POWER_READY shell_pid=%s crash_count=%s\\n' \$shell_pid \$crash_count"
  response="$(qga_request "$socket" "{\"execute\":\"guest-exec\",\"arguments\":{\"path\":\"/bin/sh\",\"arg\":[\"-lc\",\"$command\"],\"capture-output\":true}}")"
  pid="$(sed -n 's/.*"pid": \?\([0-9]*\).*/\1/p' <<<"$response")"
  if [[ -z "$pid" ]]; then
    echo "Could not start the disposable onboarding setup command." >&2
    return 1
  fi

  deadline=$((SECONDS + boot_timeout))
  while (( SECONDS < deadline )); do
    status="$(qga_request "$socket" "{\"execute\":\"guest-exec-status\",\"arguments\":{\"pid\":$pid}}" || true)"
    if grep -Fq '"exited": true' <<<"$status"; then
      if grep -Eq '"exitcode": 0([,}])' <<<"$status"; then
        # The guest command itself is fail-fast and exits successfully only
        # after finding the shell, its session log, and zero unexpected stops.
        # QGA's optional base64 stdout can be truncated by older agents, so do
        # not weaken that exit-status proof by reparsing informational output.
        power_ready_evidence="DAISYOS_POWER_READY shell_pid=verified crash_count=0"
        return 0
      fi
      echo "Could not prepare the disposable desktop for power testing." >&2
      encoded="$(sed -n 's/.*"err-data": "\([^"]*\)".*/\1/p' <<<"$status")"
      output="$(printf '%s' "$encoded" | base64 -d 2>/dev/null || true)"
      printf 'Guest response: %s\n' "$status" >&2
      [[ -z "$output" ]] || printf '%s\n' "$output" >&2
      return 1
    fi
    sleep 1
  done

  echo "Timed out while preparing the disposable desktop for power testing." >&2
  return 1
}

if [[ -z "$iso_path" || ! -f "$iso_path" ]]; then
  echo "No ISO was found. Build one with sudo scripts/build-iso.sh first." >&2
  exit 1
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
if [[ -z "$power_timeout" ]]; then
  if [[ "$acceleration" == "KVM" ]]; then
    power_timeout=90
  else
    power_timeout=240
  fi
fi

vm_is_running() {
  local pid_file="$1"
  local pid
  [[ -f "$pid_file" ]] || return 1
  pid="$(<"$pid_file")"
  [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null
}

run_action() {
  local action="$1"
  local artifact_dir="$repo_root/artifacts/iso-power-$action"
  local serial_log="$artifact_dir/serial.log"
  local monitor_socket="$artifact_dir/monitor.sock"
  local qga_socket="$artifact_dir/qga.sock"
  local qmp_socket="$artifact_dir/qmp.sock"
  local pid_file="$artifact_dir/qemu.pid"
  local menu_frame="$artifact_dir/power-menu.ppm"
  local menu_png="$artifact_dir/power-menu.png"
  local after_click_frame="$artifact_dir/after-click.ppm"
  local after_click_png="$artifact_dir/after-click.png"
  local positioned_frame="$artifact_dir/pointer-positioned.ppm"
  local positioned_png="$artifact_dir/pointer-positioned.png"
  local click_x=930
  local click_y
  local absolute_x absolute_y qmp_position qmp_down qmp_up qmp_position_response qmp_click_response running_pid
  local dimensions
  local deadline
  local color_count=0

  power_ready_evidence=""

  case "$action" in
    reboot) click_y=660 ;;
    shutdown) click_y=626 ;;
  esac

  rm -rf "$artifact_dir"
  mkdir -p "$artifact_dir"

  printf 'Testing DaisyOS %s through BIOS with %s.\n' "$action" "$acceleration"
  # Keep lifecycle validation independent of the known OVMF/virtio-vga path.
  # GPU-device coverage belongs in a separate graphics test.
  qemu-system-x86_64 \
    "${machine_args[@]}" \
    -name "DaisyOS-ISO-Power-$action" \
    -m 4096 \
    -smp 4 \
    -vga std \
    -device qemu-xhci \
    -device usb-tablet \
    -chardev "socket,path=$qga_socket,server=on,wait=off,id=qga0" \
    -device virtio-serial \
    -device "virtserialport,chardev=qga0,name=org.qemu.guest_agent.0" \
    -cdrom "$iso_path" \
    -boot once=d \
    -display none \
    -serial "file:$serial_log" \
    -monitor "unix:$monitor_socket,server=on,wait=off" \
    -qmp "unix:$qmp_socket,server=on,wait=off" \
    -no-reboot \
    -daemonize \
    -pidfile "$pid_file"
  active_pid_file="$pid_file"

  if ! provision_completed_onboarding "$qga_socket"; then
    kill "$(cat "$pid_file")" 2>/dev/null || true
    return 1
  fi

  deadline=$((SECONDS + boot_timeout))
  while (( SECONDS < deadline )); do
    if grep -q 'Reached target.*Graphical Interface' "$serial_log" 2>/dev/null; then
      break
    fi
    if [[ ! -f "$pid_file" ]] || ! kill -0 "$(cat "$pid_file")" 2>/dev/null; then
      echo "QEMU stopped before the graphical session was ready." >&2
      return 1
    fi
    sleep 2
  done
  if ! grep -q 'Reached target.*Graphical Interface' "$serial_log"; then
    echo "The power-test VM did not reach graphical.target." >&2
    kill "$(cat "$pid_file")" 2>/dev/null || true
    return 1
  fi

  deadline=$((SECONDS + render_timeout))
  while (( SECONDS < deadline )); do
    printf 'screendump %s\n' "$menu_frame" | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null
    color_count="$(magick "$menu_frame" -format '%k' info:)"
    if [[ "$color_count" =~ ^[0-9]+$ ]] && (( color_count > 256 )); then
      break
    fi
    sleep 5
  done
  if [[ ! "$color_count" =~ ^[0-9]+$ ]] || (( color_count <= 256 )); then
    echo "DaisyOS did not render before the $action test." >&2
    kill "$(cat "$pid_file")" 2>/dev/null || true
    return 1
  fi

  if [[ "$power_ready_evidence" != "DAISYOS_POWER_READY shell_pid=verified crash_count=0" ]]; then
    echo "The shell did not report a clean session before the power-menu test." >&2
    kill "$(cat "$pid_file")" 2>/dev/null || true
    return 1
  fi

  printf 'sendkey alt-f4\n' | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null
  sleep 3
  printf 'mouse_move 20 0\nscreendump %s\n' "$menu_frame" \
    | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null
  magick "$menu_frame" "$menu_png"

  dimensions="$(magick identify -format '%wx%h' "$menu_png")"
  if [[ "$dimensions" != "1280x800" ]]; then
    echo "Power-button automation requires the validated 1280x800 live layout; got $dimensions." >&2
    kill "$(cat "$pid_file")" 2>/dev/null || true
    return 1
  fi

  # The launcher's search box deliberately retains keyboard focus when the
  # power menu opens, so Tab order is not a stable automation contract. The
  # test already enforces the validated 1280x800 layout. QMP's absolute input
  # API maps that pixel position to the HID tablet's 0..32767 coordinate space.
  absolute_x=$((click_x * 32767 / 1280))
  absolute_y=$((click_y * 32767 / 800))
  qmp_position="{\"execute\":\"input-send-event\",\"arguments\":{\"events\":[{\"type\":\"abs\",\"data\":{\"axis\":\"x\",\"value\":$absolute_x}},{\"type\":\"abs\",\"data\":{\"axis\":\"y\",\"value\":$absolute_y}}]}}"
  qmp_down='{"execute":"input-send-event","arguments":{"events":[{"type":"btn","data":{"button":"left","down":true}}]}}'
  qmp_up='{"execute":"input-send-event","arguments":{"events":[{"type":"btn","data":{"button":"left","down":false}}]}}'
  qmp_position_response="$({
    printf '%s\n' '{"execute":"qmp_capabilities"}' "$qmp_position"
    sleep 1
  } | socat - "UNIX-CONNECT:$qmp_socket")"
  if [[ "$(grep -Fc '"return": {}' <<<"$qmp_position_response")" -lt 2 ]]; then
    echo "QEMU did not accept the absolute power-menu pointer position." >&2
    return 1
  fi

  sleep 1
  printf 'screendump %s\n' "$positioned_frame" \
    | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null
  magick "$positioned_frame" "$positioned_png"

  qmp_click_response="$({
    printf '%s\n' '{"execute":"qmp_capabilities"}' "$qmp_down"
    sleep 0.2
    printf '%s\n' "$qmp_up"
    sleep 1
  } | socat - "UNIX-CONNECT:$qmp_socket")"
  if [[ "$(grep -Fc '"return": {}' <<<"$qmp_click_response")" -lt 3 ]]; then
    echo "QEMU did not accept the absolute power-menu click." >&2
    return 1
  fi

  sleep 3
  if vm_is_running "$pid_file"; then
    if printf 'screendump %s\n' "$after_click_frame" \
      | socat - "UNIX-CONNECT:$monitor_socket" >/dev/null 2>&1; then
      magick "$after_click_frame" "$after_click_png"
    fi
  fi

  # -no-reboot makes both a guest reboot and a guest poweroff terminate this
  # disposable QEMU process, giving the test an unambiguous lifecycle signal.
  deadline=$((SECONDS + power_timeout))
  while (( SECONDS < deadline )); do
    if ! vm_is_running "$pid_file"; then
      break
    fi
    sleep 1
  done
  if vm_is_running "$pid_file"; then
    running_pid="$(cat "$pid_file" 2>/dev/null || true)"
    echo "The $action action did not stop the disposable VM within ${power_timeout}s." >&2
    if [[ "$running_pid" =~ ^[0-9]+$ ]]; then
      kill "$running_pid" 2>/dev/null || true
    fi
    return 1
  fi

  active_pid_file=""
  printf 'action=%s\nresult=passed\nevidence=%s\n' \
    "$action" "$menu_png" > "$artifact_dir/result.txt"
  printf 'DaisyOS %s completed through the real power menu. Evidence: %s\n' "$action" "$menu_png"
}

for action in "${actions[@]}"; do
  run_action "$action"
done
