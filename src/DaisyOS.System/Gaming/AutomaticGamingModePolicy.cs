namespace DaisyOS.System.Gaming;

/// <summary>
/// Decides when Gaming Mode should follow running games. It only ever turns off
/// Gaming Mode that it turned on itself, and if the user switches Gaming Mode off
/// during a game it stays off until that game exits.
/// </summary>
public sealed class AutomaticGamingModePolicy
{
    private bool _enabledByPolicy;
    private bool _dismissedForCurrentGame;

    /// <returns>The Gaming Mode value to apply, or null to leave it unchanged.</returns>
    public bool? Evaluate(bool gameRunning, bool automaticEnabled, bool gamingModeOn)
    {
        if (!gameRunning)
        {
            _dismissedForCurrentGame = false;
            if (!_enabledByPolicy) return null;
            _enabledByPolicy = false;
            return gamingModeOn ? false : null;
        }

        if (_enabledByPolicy)
        {
            if (!gamingModeOn)
            {
                _enabledByPolicy = false;
                _dismissedForCurrentGame = true;
                return null;
            }
            if (!automaticEnabled)
            {
                _enabledByPolicy = false;
                return false;
            }
            return null;
        }

        if (automaticEnabled && !gamingModeOn && !_dismissedForCurrentGame)
        {
            _enabledByPolicy = true;
            return true;
        }
        return null;
    }
}
