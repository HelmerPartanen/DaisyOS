namespace DaisyOS.Core.Models;

public sealed record AudioAppStreamInfo(
    string Id,
    string Name,
    double Volume,
    bool IsMuted);
