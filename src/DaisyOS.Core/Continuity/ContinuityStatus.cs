namespace DaisyOS.Core.Continuity;

public sealed record ContinuityCommand(string Action, string Id = "", string Value = "", bool Allowed = false);
public sealed record ContinuityDevice(string Id, string Name, string Address, string Status, bool Trusted, bool Clipboard, bool Files);
public sealed record ContinuityPrompt(string Id, string Kind, string PeerId, string Name, string Detail);
public sealed record ContinuityTransfer(string Id, string PeerId, string Name, long Length, long Completed, string Status);
public sealed record ContinuityStatus(bool Enabled, bool Visible, bool Paused, bool SessionReady, string Name, string DeviceId,
    string ClipboardStatus, string Diagnostic, ContinuityDevice[] Devices, ContinuityPrompt[] Prompts, ContinuityTransfer[] Transfers);
