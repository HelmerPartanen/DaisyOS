# Continuity media/handoff changeset

This extension adds focused viewer keyboard/mouse control, screen preview, Notes text activity handoff, and remote audio output selection with the Continuity icon. See [MEDIA_AND_HANDOFF.md](MEDIA_AND_HANDOFF.md) for the supported scope and hardware test checklist.

Exact source/documentation files changed in this extension:

- `docs/continuity/MEDIA_AND_HANDOFF.md`
- `docs/continuity/MEDIA_CHANGESET.md`
- `docs/continuity/OPERATIONS.md`
- `docs/continuity/PROTOCOL.md`
- `docs/continuity/REPO_INTEGRATION.md`
- `docs/continuity/SECURITY.md`
- `docs/continuity/TEST_MATRIX.md`
- `docs/continuity/TWO_DEVICE_TEST.md`
- `docs/development/developer-commands.md`
- `packaging/arch/PKGBUILD`
- `protocol/continuity/v1/continuity.proto`
- `scripts/build-continuity-media.sh`
- `scripts/build-continuity.sh`
- `scripts/install-continuity.sh`
- `scripts/publish-shell.sh`
- `scripts/test-continuity-media.py`
- `src/DaisyOS.Continuity/AudioFeature.cs`
- `src/DaisyOS.Continuity/AvahiDiscovery.cs`
- `src/DaisyOS.Continuity/ContinuityDaemon.cs`
- `src/DaisyOS.Continuity/Generated/Continuity.cs`
- `src/DaisyOS.Continuity/HandoffFeature.cs`
- `src/DaisyOS.Continuity/LiveFeature.cs`
- `src/DaisyOS.Continuity/LocalIpc.cs`
- `src/DaisyOS.Continuity/PeerWire.cs`
- `src/DaisyOS.Continuity/SecureState.cs`
- `src/DaisyOS.Core/Continuity/ContinuityActivity.cs`
- `src/DaisyOS.Core/Continuity/ContinuityStatus.cs`
- `src/DaisyOS.Core/Continuity/IContinuityClient.cs`
- `src/DaisyOS.Core/Serialization/DaisyCoreJsonContext.cs`
- `src/DaisyOS.Notes/Program.cs`
- `src/DaisyOS.Shell/Apps/Notes/NotesView.axaml`
- `src/DaisyOS.Shell/Apps/Notes/NotesView.axaml.cs`
- `src/DaisyOS.Shell/Apps/Notes/NotesWindow.axaml.cs`
- `src/DaisyOS.Shell/Apps/System/Settings/SettingsView.Continuity.cs`
- `src/DaisyOS.Shell/Resources/Messages.fi.resx`
- `src/DaisyOS.Shell/Resources/Messages.resx`
- `src/DaisyOS.Shell/Views/Components/QuickSettings/QuickSettings.axaml.cs`
- `src/DaisyOS.Shell/Views/Continuity/ContinuityViewer.cs`
- `src/DaisyOS.System/Audio/LinuxAudioService.cs`
- `src/DaisyOS.System/Continuity/ContinuityClient.cs`
- `src/native/daisy_continuity_media/main.c`
- `tests/DaisyOS.Continuity.Tests/AudioRouteTests.cs`
- `tests/DaisyOS.Continuity.Tests/LiveFeatureTests.cs`
- `tests/DaisyOS.Tests/ContinuityAudioDeviceTests.cs`
