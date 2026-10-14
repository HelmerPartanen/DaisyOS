#
# ~/.bash_profile
#

[[ -f ~/.bashrc ]] && . ~/.bashrc

# Start the compositor-owned DaisyOS desktop on the live console. The session
# runner retains the validated Xorg path as an automatic recovery fallback.
if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" && "$(tty)" == "/dev/tty1" ]]; then
  exec /usr/local/bin/daisyos-start-session
fi
