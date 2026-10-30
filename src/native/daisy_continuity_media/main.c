#define _POSIX_C_SOURCE 200809L
#include <gio/gio.h>
#include <gio/gunixfdlist.h>
#include <gst/gst.h>
#include <gst/app/gstappsink.h>
#include <libei.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <unistd.h>
#include <poll.h>
#include <fcntl.h>
#include <math.h>

#define DESKTOP "org.freedesktop.portal.Desktop"
#define PATH "/org/freedesktop/portal/desktop"
static GDBusConnection *bus;
static gboolean running = TRUE;
static char *session;
static GVariant *response;
static const char *waiting_path;
static struct ei *input;
static struct ei_device *devices[16];
static gboolean resumed[16];
static unsigned int serial = 1;
static gboolean held_keys[16][768], held_buttons[16][8];
static int target_x, target_y;
static GVariant *empty(void) { return g_variant_new_array(G_VARIANT_TYPE("{sv}"), NULL, 0); }
static GVariant *call(const char *iface, const char *method, GVariant *args) {
    GError *error = NULL;
    GVariant *reply = g_dbus_connection_call_sync(bus, DESKTOP, PATH, iface, method, args, NULL,
        G_DBUS_CALL_FLAGS_NONE, 10000, NULL, &error);
    if (!reply) { fprintf(stderr, "Continuity portal %s failed: %s\n", method, error->message); g_error_free(error); }
    return reply;
}
static void signal_received(GDBusConnection *connection, const gchar *sender, const gchar *path,
        const gchar *iface, const gchar *member, GVariant *parameters, gpointer data) {
    (void)connection; (void)sender; (void)data;
    if (!strcmp(iface, "org.freedesktop.portal.Session") && !strcmp(member, "Closed") && session && !strcmp(path, session)) running = FALSE;
    if (!strcmp(member, "Response") && waiting_path && !strcmp(path, waiting_path) && !response) response = g_variant_ref(parameters);
}
static GVariant *request(const char *iface, const char *method, const char *token, GVariant *args) {
    const char *unique = g_dbus_connection_get_unique_name(bus);
    char *sender = g_strdup(unique + 1); for (char *p = sender; *p; p++) if (*p == '.') *p = '_';
    char *path = g_strdup_printf("/org/freedesktop/portal/desktop/request/%s/%s", sender, token);
    g_free(sender); waiting_path = path; response = NULL;
    GVariant *reply = call(iface, method, args);
    if (!reply) { waiting_path = NULL; g_free(path); return NULL; }
    g_variant_unref(reply);
    gint64 end = g_get_monotonic_time() + 120 * G_USEC_PER_SEC;
    while (running && !response && g_get_monotonic_time() < end) {
        while (g_main_context_iteration(NULL, FALSE)) {}
        struct pollfd fd = { STDIN_FILENO, POLLHUP, 0 }; poll(&fd, 1, 10);
        if (fd.revents & (POLLHUP | POLLERR)) running = FALSE;
    }
    waiting_path = NULL; g_free(path);
    if (!response) return NULL;
    guint code; GVariant *results; g_variant_get(response, "(u@a{sv})", &code, &results);
    g_variant_unref(response); response = NULL;
    if (code) { g_variant_unref(results); return NULL; }
    return results;
}
static void dispatch_input(void) {
    if (!input) return;
    ei_dispatch(input);
    struct ei_event *event;
    while ((event = ei_get_event(input))) {
        enum ei_event_type type = ei_event_get_type(event);
        struct ei_device *device = ei_event_get_device(event);
        if (type == EI_EVENT_DISCONNECT) running = FALSE;
        else if (type == EI_EVENT_SEAT_ADDED)
            ei_seat_bind_capabilities(ei_event_get_seat(event), EI_DEVICE_CAP_KEYBOARD, EI_DEVICE_CAP_POINTER,
                EI_DEVICE_CAP_POINTER_ABSOLUTE, EI_DEVICE_CAP_BUTTON, EI_DEVICE_CAP_SCROLL, NULL);
        else if (type == EI_EVENT_DEVICE_ADDED) {
            for (int i = 0; i < 16; i++) if (!devices[i]) { devices[i] = ei_device_ref(device); break; }
        } else for (int i = 0; i < 16; i++) if (devices[i] == device) {
            if (type == EI_EVENT_DEVICE_RESUMED) { memset(held_keys[i], 0, sizeof held_keys[i]); memset(held_buttons[i], 0, sizeof held_buttons[i]); resumed[i] = TRUE; ei_device_start_emulating(device, serial++); }
            if (type == EI_EVENT_DEVICE_PAUSED) { resumed[i] = FALSE; memset(held_keys[i], 0, sizeof held_keys[i]); memset(held_buttons[i], 0, sizeof held_buttons[i]); }
            if (type == EI_EVENT_DEVICE_REMOVED) { ei_device_unref(device); devices[i] = NULL; resumed[i] = FALSE; }
        }
        ei_event_unref(event);
    }
}
// Integers/finite numbers only; stdin is a private daemon pipe, never a shell.
static void inject(const char *line) {
    unsigned kind, code, state; double x, y; char extra;
    if (sscanf(line, "%u %u %u %lf %lf %c", &kind, &code, &state, &x, &y, &extra) != 5 ||
        !isfinite(x) || !isfinite(y) || fabs(x) > 8192 || fabs(y) > 8192 || state > 1) { running = FALSE; return; }
    for (int i = 0; i < 16; i++) if (devices[i] && resumed[i]) {
        struct ei_device *d = devices[i]; gboolean used = TRUE;
        if (kind == 1 && code <= 767 && ei_device_has_capability(d, EI_DEVICE_CAP_KEYBOARD)) { if (held_keys[i][code] == (gboolean)state) used = FALSE; else { held_keys[i][code] = state; ei_device_keyboard_key(d, code, state); } }
        else if (kind == 2 && code >= 272 && code <= 279 && ei_device_has_capability(d, EI_DEVICE_CAP_BUTTON)) { if (held_buttons[i][code-272] == (gboolean)state) used = FALSE; else { held_buttons[i][code-272] = state; ei_device_button_button(d, code, state); } }
        else if (kind == 3 && ei_device_has_capability(d, EI_DEVICE_CAP_POINTER)) ei_device_pointer_motion(d, x, y);
        else if (kind == 4 && ei_device_has_capability(d, EI_DEVICE_CAP_SCROLL)) ei_device_scroll_delta(d, x, y);
        else if (kind == 5 && x >= 0 && x <= 1 && y >= 0 && y <= 1 && ei_device_has_capability(d, EI_DEVICE_CAP_POINTER_ABSOLUTE)) {
            struct ei_region *r = NULL;
            for (size_t region = 0; region < 64; region++) {
                struct ei_region *candidate = ei_device_get_region(d, region);
                if (!candidate) break;
                if ((int)ei_region_get_x(candidate) == target_x && (int)ei_region_get_y(candidate) == target_y) { r = candidate; break; }
            }
            if (r) ei_device_pointer_motion_absolute(d, ei_region_get_x(r) + x * (ei_region_get_width(r) - 1), ei_region_get_y(r) + y * (ei_region_get_height(r) - 1));
            else used = FALSE;
        } else used = FALSE;
        if (used) ei_device_frame(d, (uint64_t)g_get_monotonic_time());
    }
}
static gboolean write_frame(const void *data, size_t size) {
    uint32_t header = GUINT32_TO_BE((uint32_t)size);
    return fwrite(&header, 1, 4, stdout) == 4 && (!size || fwrite(data, 1, size, stdout) == size) && !fflush(stdout);
}
#include "edge.c"

int main(int argc, char **argv) {
    if (argc >= 2 && (!strcmp(argv[1], "--capture") || !strcmp(argv[1], "--input"))) return edge_main(argc, argv);
    if (argc != 2 || (strcmp(argv[1], "--view") && strcmp(argv[1], "--control"))) return 2;
    gboolean control = !strcmp(argv[1], "--control");
    gst_init(NULL, NULL);
    bus = g_bus_get_sync(G_BUS_TYPE_SESSION, NULL, NULL); if (!bus) return 1;
    guint watch = g_dbus_connection_signal_subscribe(bus, DESKTOP, NULL, NULL, NULL, NULL, G_DBUS_SIGNAL_FLAGS_NONE, signal_received, NULL, NULL);
    const char *iface = control ? "org.freedesktop.portal.RemoteDesktop" : "org.freedesktop.portal.ScreenCast";
    char token[96]; const char *grant = getenv("DAISY_CONTINUITY_GRANT");
    if (grant) snprintf(token, sizeof token, "continuity_%s", grant); else snprintf(token, sizeof token, "continuity_%d", getpid());
    GVariantBuilder opts; g_variant_builder_init(&opts, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(&opts, "{sv}", "handle_token", g_variant_new_string(token));
    g_variant_builder_add(&opts, "{sv}", "session_handle_token", g_variant_new_string(token));
    GVariant *result = request(iface, "CreateSession", token, g_variant_new("(a{sv})", &opts));
    if (!result) return 1;
    g_variant_lookup(result, "session_handle", "s", &session); g_variant_unref(result); if (!session) return 1;
    int status = 1; GstElement *pipeline = NULL; GUnixFDList *fds = NULL;
    if (control) {
        g_variant_builder_init(&opts, G_VARIANT_TYPE_VARDICT);
        g_variant_builder_add(&opts, "{sv}", "handle_token", g_variant_new_string("devices"));
        g_variant_builder_add(&opts, "{sv}", "types", g_variant_new_uint32(3));
        result = request(iface, "SelectDevices", "devices", g_variant_new("(oa{sv})", session, &opts));
        if (!result) goto cleanup; g_variant_unref(result);
    }
    g_variant_builder_init(&opts, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(&opts, "{sv}", "handle_token", g_variant_new_string("sources"));
    g_variant_builder_add(&opts, "{sv}", "types", g_variant_new_uint32(1));
    g_variant_builder_add(&opts, "{sv}", "multiple", g_variant_new_boolean(FALSE));
    g_variant_builder_add(&opts, "{sv}", "cursor_mode", g_variant_new_uint32(2));
    result = request("org.freedesktop.portal.ScreenCast", "SelectSources", "sources", g_variant_new("(oa{sv})", session, &opts));
    if (!result) goto cleanup; g_variant_unref(result);
    g_variant_builder_init(&opts, G_VARIANT_TYPE_VARDICT);
    g_variant_builder_add(&opts, "{sv}", "handle_token", g_variant_new_string("start"));
    result = request(iface, "Start", "start", g_variant_new("(osa{sv})", session, "", &opts));
    if (!result) goto cleanup;
    guint granted = 0; if (control && (!g_variant_lookup(result, "devices", "u", &granted) || (granted & 3) != 3)) { g_variant_unref(result); goto cleanup; }
    GVariant *streams = g_variant_lookup_value(result, "streams", G_VARIANT_TYPE("a(ua{sv})"));
    if (!streams || g_variant_n_children(streams) != 1) { if (streams) g_variant_unref(streams); g_variant_unref(result); goto cleanup; }
    GVariant *stream = g_variant_get_child_value(streams, 0); guint node; GVariant *properties;
    g_variant_get(stream, "(u@a{sv})", &node, &properties);
    g_variant_lookup(properties, "position", "(ii)", &target_x, &target_y);
    g_variant_unref(properties); g_variant_unref(stream); g_variant_unref(streams); g_variant_unref(result);
    if (control) {
        GVariant *reply = g_dbus_connection_call_with_unix_fd_list_sync(bus, DESKTOP, PATH, iface, "ConnectToEIS",
            g_variant_new("(o@a{sv})", session, empty()), G_VARIANT_TYPE("(h)"), G_DBUS_CALL_FLAGS_NONE, 10000, NULL, &fds, NULL, NULL);
        if (!reply || !fds) { if (reply) g_variant_unref(reply); goto cleanup; }
        gint handle; g_variant_get(reply, "(h)", &handle); g_variant_unref(reply);
        int fd = g_unix_fd_list_get(fds, handle, NULL); g_clear_object(&fds); if (fd < 0) goto cleanup;
        input = ei_new_sender(NULL); ei_configure_name(input, "DaisyOS Continuity");
        if (ei_setup_backend_fd(input, fd) != 0) { close(fd); goto cleanup; }
    }
    GVariant *reply = g_dbus_connection_call_with_unix_fd_list_sync(bus, DESKTOP, PATH, "org.freedesktop.portal.ScreenCast", "OpenPipeWireRemote",
        g_variant_new("(o@a{sv})", session, empty()), G_VARIANT_TYPE("(h)"), G_DBUS_CALL_FLAGS_NONE, 10000, NULL, &fds, NULL, NULL);
    if (!reply || !fds) { if (reply) g_variant_unref(reply); goto cleanup; }
    gint handle; g_variant_get(reply, "(h)", &handle); g_variant_unref(reply);
    int fd = g_unix_fd_list_get(fds, handle, NULL); g_clear_object(&fds); if (fd < 0) goto cleanup;
    char description[512]; snprintf(description, sizeof description,
        "pipewiresrc name=source fd=%d path=%u do-timestamp=true ! videoconvert ! videoscale ! videorate ! video/x-raw,width=1280,height=720,framerate=10/1 ! jpegenc quality=70 ! appsink name=frames max-buffers=1 drop=true sync=false", fd, node);
    GError *error = NULL; pipeline = gst_parse_launch(description, &error);
    if (error || !pipeline) { if (error) g_error_free(error); close(fd); goto cleanup; }
    GstElement *sink = gst_bin_get_by_name(GST_BIN(pipeline), "frames");
    GstBus *messages = gst_element_get_bus(pipeline);
    gst_element_set_state(pipeline, GST_STATE_PLAYING);
    fcntl(STDIN_FILENO, F_SETFL, O_NONBLOCK);
    char buffer[1024]; size_t used = 0;
    gint64 last_frame = g_get_monotonic_time();
    if (!write_frame(NULL, 0)) running = FALSE;
    while (running) {
        while (g_main_context_iteration(NULL, FALSE)) {}
        if (g_dbus_connection_is_closed(bus)) break;
        dispatch_input();
        struct pollfd stdin_fd = { STDIN_FILENO, POLLIN | POLLHUP, 0 }; poll(&stdin_fd, 1, 0);
        if (stdin_fd.revents & (POLLHUP | POLLERR)) break;
        if (stdin_fd.revents & POLLIN) {
            ssize_t n = read(STDIN_FILENO, buffer + used, sizeof buffer - used - 1);
            if (n <= 0) break;
            used += (size_t)n; buffer[used] = 0;
            char *newline;
            while ((newline = strchr(buffer, '\n'))) {
                *newline = 0; if (control) inject(buffer); else running = FALSE;
                size_t consumed = (size_t)(newline - buffer) + 1; memmove(buffer, buffer + consumed, used - consumed); used -= consumed; buffer[used] = 0;
            }
            if (used == sizeof buffer - 1) break;
        }
        GstMessage *message = gst_bus_pop_filtered(messages, GST_MESSAGE_ERROR | GST_MESSAGE_EOS);
        if (message) { gst_message_unref(message); break; }
        GstSample *sample = gst_app_sink_try_pull_sample(GST_APP_SINK(sink), 20 * GST_MSECOND);
        if (sample) {
            GstMapInfo map; GstBuffer *frame = gst_sample_get_buffer(sample);
            if (gst_buffer_map(frame, &map, GST_MAP_READ)) {
                if (map.size > 512 * 1024 || !write_frame(map.data, map.size)) running = FALSE;
                gst_buffer_unmap(frame, &map);
            }
            gst_sample_unref(sample); last_frame = g_get_monotonic_time();
        } else if (g_get_monotonic_time() - last_frame > 2 * G_USEC_PER_SEC) {
            if (!write_frame(NULL, 0)) running = FALSE;
            last_frame = g_get_monotonic_time();
        }
    }
    gst_object_unref(messages); gst_object_unref(sink); close(fd); status = 0;
cleanup:
    if (pipeline) { gst_element_set_state(pipeline, GST_STATE_NULL); gst_object_unref(pipeline); }
    if (input) {
        for (int i = 0; i < 16; i++) if (devices[i]) { if (resumed[i]) ei_device_stop_emulating(devices[i]); ei_device_unref(devices[i]); }
        ei_unref(input);
    }
    if (session) {
        GVariant *closed = g_dbus_connection_call_sync(bus, DESKTOP, session, "org.freedesktop.portal.Session", "Close", NULL, NULL, G_DBUS_CALL_FLAGS_NONE, 2000, NULL, NULL);
        if (closed) g_variant_unref(closed); g_free(session);
    }
    g_clear_object(&fds); g_dbus_connection_signal_unsubscribe(bus, watch); g_object_unref(bus);
    return status;
}
