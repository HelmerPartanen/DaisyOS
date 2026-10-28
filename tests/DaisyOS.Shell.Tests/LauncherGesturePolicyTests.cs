using System;
using DaisyOS.Shell.Services;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class LauncherGesturePolicyTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstMetaPressOverFullscreenOnlyArmsTheGesture()
    {
        var policy = new LauncherGesturePolicy();

        var decision = policy.Decide(
            LauncherRequestSource.Meta,
            isLauncherOpen: false,
            isFullscreenAppActive: true,
            Start);

        Assert.Equal(LauncherGestureDecision.Arm, decision);
        Assert.True(policy.IsArmed);
    }

    [Fact]
    public void SecondMetaPressWithinConfirmationWindowOpens()
    {
        var policy = new LauncherGesturePolicy();
        policy.Decide(LauncherRequestSource.Meta, false, true, Start);

        var decision = policy.Decide(
            LauncherRequestSource.Meta,
            false,
            true,
            Start + LauncherGesturePolicy.ConfirmationWindow);

        Assert.Equal(LauncherGestureDecision.Open, decision);
        Assert.False(policy.IsArmed);
    }

    [Fact]
    public void MetaPressAfterConfirmationWindowArmsAgain()
    {
        var policy = new LauncherGesturePolicy();
        policy.Decide(LauncherRequestSource.Meta, false, true, Start);

        var decision = policy.Decide(
            LauncherRequestSource.Meta,
            false,
            true,
            Start + LauncherGesturePolicy.ConfirmationWindow + TimeSpan.FromMilliseconds(1));

        Assert.Equal(LauncherGestureDecision.Arm, decision);
        Assert.True(policy.IsArmed);
    }

    [Fact]
    public void MetaPressOnDesktopOpensImmediately()
    {
        var policy = new LauncherGesturePolicy();

        var decision = policy.Decide(LauncherRequestSource.Meta, false, false, Start);

        Assert.Equal(LauncherGestureDecision.Open, decision);
        Assert.False(policy.IsArmed);
    }

    [Fact]
    public void ExplicitRequestBypassesFullscreenGuard()
    {
        var policy = new LauncherGesturePolicy();

        var decision = policy.Decide(LauncherRequestSource.Explicit, false, true, Start);

        Assert.Equal(LauncherGestureDecision.Open, decision);
        Assert.False(policy.IsArmed);
    }

    [Fact]
    public void MetaClosesAnOpenLauncherImmediately()
    {
        var policy = new LauncherGesturePolicy();
        policy.Decide(LauncherRequestSource.Meta, false, true, Start);

        var decision = policy.Decide(LauncherRequestSource.Meta, true, true, Start);

        Assert.Equal(LauncherGestureDecision.Close, decision);
        Assert.False(policy.IsArmed);
    }

    [Fact]
    public void ExplicitRequestClearsAnArmedGesture()
    {
        var policy = new LauncherGesturePolicy();
        policy.Decide(LauncherRequestSource.Meta, false, true, Start);

        policy.Decide(LauncherRequestSource.Explicit, false, true, Start);

        Assert.False(policy.IsArmed);
        var next = policy.Decide(
            LauncherRequestSource.Meta,
            false,
            true,
            Start + TimeSpan.FromMilliseconds(10));
        Assert.Equal(LauncherGestureDecision.Arm, next);
    }

    [Fact]
    public void ArmDoesNotSurviveACompletedClose()
    {
        var policy = new LauncherGesturePolicy();
        policy.Decide(LauncherRequestSource.Meta, false, true, Start);

        // A close request from anywhere (Escape, an app launch, or the
        // fullscreen transition) must discard the pending confirmation.
        policy.Decide(LauncherRequestSource.Meta, true, true, Start);

        var next = policy.Decide(
            LauncherRequestSource.Meta,
            false,
            true,
            Start + TimeSpan.FromMilliseconds(10));
        Assert.Equal(LauncherGestureDecision.Arm, next);
    }
}
