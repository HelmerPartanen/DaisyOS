/* SPDX-License-Identifier: MIT
 * Only adapters for scanner-generated static inline functions. All monitor
 * state, lifecycle, threading and event-loop logic lives in Rust.
 */
#include <wayland-client.h>
#include "idle.h"
struct wl_registry *daisy_registry(struct wl_display *d) { return wl_display_get_registry(d); }
int daisy_registry_listen(struct wl_registry *r, const struct wl_registry_listener *l, void *d) { return wl_registry_add_listener(r,l,d); }
void *daisy_bind(struct wl_registry *r, uint32_t id, int seat) { return wl_registry_bind(r,id,seat?&wl_seat_interface:&ext_idle_notifier_v1_interface,1); }
struct ext_idle_notification_v1 *daisy_notification(struct ext_idle_notifier_v1 *n, uint32_t timeout, struct wl_seat *s) { return ext_idle_notifier_v1_get_idle_notification(n,timeout,s); }
int daisy_notification_listen(struct ext_idle_notification_v1 *n, const struct ext_idle_notification_v1_listener *l, void *d) { return ext_idle_notification_v1_add_listener(n,l,d); }
void daisy_destroy_notification(struct ext_idle_notification_v1 *n) { ext_idle_notification_v1_destroy(n); }
void daisy_destroy_notifier(struct ext_idle_notifier_v1 *n) { ext_idle_notifier_v1_destroy(n); }
void daisy_destroy_seat(struct wl_seat *s) { wl_seat_destroy(s); }
void daisy_destroy_registry(struct wl_registry *r) { wl_registry_destroy(r); }
