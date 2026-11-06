#pragma once

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void (*daisy_idle_callback_t)(void *user_data);

/**
 * Checks if Wayland display is accessible and ext_idle_notifier_v1 is advertised.
 * Returns 1 if available, 0 otherwise.
 */
int daisy_idle_monitor_is_available(void);

/**
 * Starts background idle monitoring.
 * @param timeout_ms Idle timeout in milliseconds.
 * @param on_idle Callback invoked when user has been idle for timeout_ms.
 * @param on_resumed Callback invoked when user activity resumes.
 * @param user_data Opaque pointer passed to callbacks.
 * Callbacks run on a native worker thread and must return promptly. Do not
 * call stop from a callback; callbacks and user_data must remain valid until
 * stop returns. Stop each handle once, with no concurrent stop calls.
 * @return Opaque handle to the monitor instance, or NULL on failure.
 */
void *daisy_idle_monitor_start(uint32_t timeout_ms,
                               daisy_idle_callback_t on_idle,
                               daisy_idle_callback_t on_resumed,
                               void *user_data);

/**
 * Stops background idle monitoring and frees resources.
 */
void daisy_idle_monitor_stop(void *handle);

#ifdef __cplusplus
}
#endif
