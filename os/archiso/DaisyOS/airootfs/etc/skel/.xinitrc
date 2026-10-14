#!/bin/sh

# Disable screen blanking, screensaver and DPMS.
xset s off
xset s noblank
xset -dpms

# KWin nested over the validated Xorg server remains the compositor fallback
# when direct DRM startup is unavailable. If KWin itself cannot start, the
# wrapper drops to the original supervised Xorg shell and records that fact.
exec /usr/local/bin/daisyos-xorg-session
