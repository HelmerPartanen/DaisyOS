/* Original C implementation retained for -DDAISY_IDLE_C_BASELINE=ON only.
 * Production builds use ../daisy_background (Rust). */
#include "daisy_idle.h"
#include "ext-idle-notify-v1-client-protocol.h"
#include <fcntl.h>
#include <poll.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <wayland-client.h>

struct idle_monitor {
    pthread_t thread;
    int pipe_fd[2];
    uint32_t timeout_ms;
    daisy_idle_callback_t on_idle;
    daisy_idle_callback_t on_resumed;
    void *user_data;
    volatile int running;
};

struct wayland_state {
    struct wl_display *display;
    struct wl_registry *registry;
    struct ext_idle_notifier_v1 *notifier;
    struct wl_seat *seat;
    struct ext_idle_notification_v1 *notification;
    struct idle_monitor *monitor;
};

static void handle_idled(void *data, struct ext_idle_notification_v1 *notification) {
    (void)notification;
    struct wayland_state *state = (struct wayland_state *)data;
    if (state && state->monitor && state->monitor->on_idle) {
        state->monitor->on_idle(state->monitor->user_data);
    }
}

static void handle_resumed(void *data, struct ext_idle_notification_v1 *notification) {
    (void)notification;
    struct wayland_state *state = (struct wayland_state *)data;
    if (state && state->monitor && state->monitor->on_resumed) {
        state->monitor->on_resumed(state->monitor->user_data);
    }
}

static const struct ext_idle_notification_v1_listener idle_listener = {
    .idled = handle_idled,
    .resumed = handle_resumed,
};

static void registry_handle_global(void *data, struct wl_registry *registry,
                                  uint32_t name, const char *interface, uint32_t version) {
    (void)version;
    struct wayland_state *state = (struct wayland_state *)data;
    if (strcmp(interface, ext_idle_notifier_v1_interface.name) == 0) {
        state->notifier = (struct ext_idle_notifier_v1 *)wl_registry_bind(
            registry, name, &ext_idle_notifier_v1_interface, 1);
    } else if (strcmp(interface, wl_seat_interface.name) == 0) {
        if (!state->seat) {
            state->seat = (struct wl_seat *)wl_registry_bind(
                registry, name, &wl_seat_interface, 1);
        }
    }
}

static void registry_handle_global_remove(void *data, struct wl_registry *registry, uint32_t name) {
    (void)data;
    (void)registry;
    (void)name;
}

static const struct wl_registry_listener registry_listener = {
    .global = registry_handle_global,
    .global_remove = registry_handle_global_remove,
};

int daisy_idle_monitor_is_available(void) {
    struct wl_display *display = wl_display_connect(NULL);
    if (!display) {
        return 0;
    }

    struct wayland_state state;
    memset(&state, 0, sizeof(state));

    state.registry = wl_display_get_registry(display);
    if (!state.registry) {
        wl_display_disconnect(display);
        return 0;
    }

    wl_registry_add_listener(state.registry, &registry_listener, &state);
    wl_display_roundtrip(display);

    int available = (state.notifier != NULL && state.seat != NULL) ? 1 : 0;

    if (state.seat) wl_seat_destroy(state.seat);
    if (state.notifier) ext_idle_notifier_v1_destroy(state.notifier);
    wl_registry_destroy(state.registry);
    wl_display_disconnect(display);

    return available;
}

static void *monitor_thread_func(void *arg) {
    struct idle_monitor *monitor = (struct idle_monitor *)arg;
    struct wayland_state state;
    memset(&state, 0, sizeof(state));
    state.monitor = monitor;

    state.display = wl_display_connect(NULL);
    if (!state.display) {
        fprintf(stderr, "[daisy-idle] Could not connect to Wayland display\n");
        return NULL;
    }

    state.registry = wl_display_get_registry(state.display);
    if (!state.registry) {
        fprintf(stderr, "[daisy-idle] Could not get Wayland registry\n");
        wl_display_disconnect(state.display);
        return NULL;
    }

    wl_registry_add_listener(state.registry, &registry_listener, &state);
    wl_display_roundtrip(state.display);

    if (!state.notifier || !state.seat) {
        fprintf(stderr, "[daisy-idle] ext_idle_notifier_v1 or wl_seat not available on compositor\n");
        if (state.seat) wl_seat_destroy(state.seat);
        if (state.notifier) ext_idle_notifier_v1_destroy(state.notifier);
        wl_registry_destroy(state.registry);
        wl_display_disconnect(state.display);
        return NULL;
    }

    state.notification = ext_idle_notifier_v1_get_idle_notification(
        state.notifier, monitor->timeout_ms, state.seat);
    if (!state.notification) {
        fprintf(stderr, "[daisy-idle] Could not create ext_idle_notification_v1\n");
        wl_seat_destroy(state.seat);
        ext_idle_notifier_v1_destroy(state.notifier);
        wl_registry_destroy(state.registry);
        wl_display_disconnect(state.display);
        return NULL;
    }

    ext_idle_notification_v1_add_listener(state.notification, &idle_listener, &state);
    wl_display_flush(state.display);

    int wl_fd = wl_display_get_fd(state.display);
    struct pollfd pfd[2];
    pfd[0].fd = wl_fd;
    pfd[0].events = POLLIN;
    pfd[1].fd = monitor->pipe_fd[0];
    pfd[1].events = POLLIN;

    while (monitor->running) {
        while (wl_display_prepare_read(state.display) != 0) {
            wl_display_dispatch_pending(state.display);
        }
        wl_display_flush(state.display);

        int ret = poll(pfd, 2, -1);
        if (ret < 0) {
            wl_display_cancel_read(state.display);
            break;
        }

        if (pfd[1].revents & POLLIN) {
            // Wakeup pipe triggered on stop, exit cleanly
            wl_display_cancel_read(state.display);
            break;
        }

        if (pfd[0].revents & POLLIN) {
            wl_display_read_events(state.display);
            wl_display_dispatch_pending(state.display);
        } else {
            wl_display_cancel_read(state.display);
        }
    }

    if (state.notification) ext_idle_notification_v1_destroy(state.notification);
    if (state.seat) wl_seat_destroy(state.seat);
    if (state.notifier) ext_idle_notifier_v1_destroy(state.notifier);
    wl_registry_destroy(state.registry);
    wl_display_disconnect(state.display);
    return NULL;
}

void *daisy_idle_monitor_start(uint32_t timeout_ms,
                               daisy_idle_callback_t on_idle,
                               daisy_idle_callback_t on_resumed,
                               void *user_data) {
    if (timeout_ms == 0) return NULL;

    struct idle_monitor *mon = (struct idle_monitor *)calloc(1, sizeof(struct idle_monitor));
    if (!mon) return NULL;

    mon->timeout_ms = timeout_ms;
    mon->on_idle = on_idle;
    mon->on_resumed = on_resumed;
    mon->user_data = user_data;
    mon->running = 1;

    if (pipe(mon->pipe_fd) != 0) {
        free(mon);
        return NULL;
    }

    // Set read end of pipe non-blocking
    int flags = fcntl(mon->pipe_fd[0], F_GETFL, 0);
    if (flags >= 0) {
        fcntl(mon->pipe_fd[0], F_SETFL, flags | O_NONBLOCK);
    }

    if (pthread_create(&mon->thread, NULL, monitor_thread_func, mon) != 0) {
        close(mon->pipe_fd[0]);
        close(mon->pipe_fd[1]);
        free(mon);
        return NULL;
    }

    return mon;
}

void daisy_idle_monitor_stop(void *handle) {
    if (!handle) return;
    struct idle_monitor *mon = (struct idle_monitor *)handle;
    mon->running = 0;
    char dummy = 'x';
    (void)write(mon->pipe_fd[1], &dummy, 1);
    pthread_join(mon->thread, NULL);
    close(mon->pipe_fd[0]);
    close(mon->pipe_fd[1]);
    free(mon);
}

#ifdef BUILD_STANDALONE
static void standalone_idle(void *ud) {
    (void)ud;
    printf("IDLE\n");
    fflush(stdout);
}

static void standalone_resumed(void *ud) {
    (void)ud;
    printf("RESUMED\n");
    fflush(stdout);
}

int main(int argc, char **argv) {
    uint32_t timeout_sec = 600;
    if (argc > 1) {
        int t = atoi(argv[1]);
        if (t > 0) timeout_sec = (uint32_t)t;
    }

    uint32_t timeout_ms = timeout_sec * 1000;
    void *h = daisy_idle_monitor_start(timeout_ms, standalone_idle, standalone_resumed, NULL);
    if (!h) {
        fprintf(stderr, "Failed to initialize idle monitor\n");
        return 1;
    }

    // Run until EOF or signal
    while (getchar() != EOF) {}
    daisy_idle_monitor_stop(h);
    return 0;
}
#endif
