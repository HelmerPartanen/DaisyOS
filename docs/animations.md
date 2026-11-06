# Animation guidelines

1. Every animation has a duration in milliseconds, never a fixed number of frames.

2. Use a monotonic clock, so system clock adjustments cannot disturb animations.

3. Render according to the display's refresh cycle, while evaluating progress from elapsed time.

4. Skip outdated frames, rather than slowing the animation down when rendering falls behind.

5. Finish animations at their intended end time, even when some intermediate frames were missed.

6. Stop unnecessary rendering when the UI is idle, preserving battery life and resources.