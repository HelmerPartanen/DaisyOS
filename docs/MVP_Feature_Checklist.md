# DaisyOS — MVP Feature Checklist

**Goal:** A polished, reliable Linux desktop for gamers and creative users without delaying the first release with features that can wait.

Use this checklist to track implementation and testing. A feature is complete only when it works reliably and has been tested.

## 1. Core operating system — Must have

- [ ] Reliable Arch-based installation and first boot
- [ ] Hardware detection and sensible driver setup
- [ ] Working Wi-Fi, Bluetooth, audio and display configuration
- [ ] Reliable system updates and recovery from failed updates
- [ ] User accounts, permissions and basic security
- [ ] Sleep, wake, shutdown and restart work correctly

## 2. Desktop shell and UI — Must have

- [ ] Custom desktop, taskbar and application launcher
- [ ] Quick settings for Wi-Fi, audio, brightness and power
- [ ] Notifications, clock, calendar and system tray
- [ ] Window management, workspaces and multi-monitor support
- [ ] Consistent design system across all native components
- [ ] Smooth animations without noticeable lag
- [ ] Light and dark themes with polished wallpapers

## 3. Gaming — Must have

- [ ] Steam preinstalled or available through one-click installation
- [ ] Proton support for compatible Windows games
- [ ] Automatic GPU driver configuration for supported hardware
- [ ] Controller support and device detection
- [ ] Game mode or performance profile
- [ ] Clear visibility into game compatibility and known limitations

## 4. Essential applications — Must have

- [ ] Native file manager with reliable file operations
- [ ] Settings app with searchable, organized categories
- [ ] Software center for installing and updating applications
- [ ] Web browser installation or a ready-to-use browser
- [ ] Screenshot and screen recording tools
- [ ] PDF viewer and basic document handling

## 5. Performance and reliability — Must have

- [ ] Fast boot and responsive desktop interactions
- [ ] Low idle CPU and memory usage
- [ ] No stuttering during normal desktop animations
- [ ] Reliable suspend/resume and graphics recovery
- [ ] Safe update strategy with rollback or recovery options
- [ ] Automated tests for critical shell and system workflows

## 6. First-run experience — Must have

- [ ] Simple installer with clear disk and partition choices
- [ ] First-boot setup for user, language, network and preferences
- [ ] Automatic hardware and driver checks
- [ ] Clear progress, errors and recovery instructions
- [ ] Desktop ready to use without terminal commands
- [ ] Basic help, diagnostics and system information

---

## Defer until after MVP

These are valuable differentiators, but should not delay the first usable release.

### Polish and customization

- [ ] Advanced frosted-glass materials and per-window effects
- [ ] Custom compositor and advanced window animations
- [ ] Desktop widgets and extensive personalization
- [ ] Dynamic wallpaper color extraction and video wallpapers

### Advanced gaming

- [ ] Automatic game-specific performance tuning
- [ ] Game library integration beyond Steam
- [ ] Advanced FPS and hardware monitoring overlay
- [ ] Optional TV/controller-first interface

### Creative and productivity

- [ ] Full-featured native PDF editor
- [ ] Creative application presets and integrations
- [ ] Cloud sync and cross-device integration
- [ ] Built-in AI assistant and local model support

---

## MVP release criteria

Do not call the MVP finished until:

- [ ] A new user can install the OS and reach a working desktop without needing a terminal.
- [ ] Essential hardware works on the supported hardware list.
- [ ] Users can install applications, manage files, configure the desktop and play supported games.
- [ ] Updates and recovery have been tested, not merely implemented.
- [ ] The shell remains responsive under normal multitasking.
- [ ] Critical workflows pass repeatable tests, and there are no known data-loss or release-blocking bugs.

## Recommended development principle

Build the complete basic experience first, from installation to launching a game. Ship the advanced glass compositor, AI features and elaborate customization only after that path is dependable.

**A smaller OS that works exceptionally well is a better MVP than a spectacular-looking shell with unfinished fundamentals.**
