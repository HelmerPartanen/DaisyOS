/* SPDX-License-Identifier: MIT
 * DaisyOS PolicyKit transport. libpolkit owns authentication and privileged helper access.
 * UI messages use base64 so passwords and prompts never become shell command arguments.
 */
#define _POSIX_C_SOURCE 200809L
#define POLKIT_AGENT_I_KNOW_API_IS_SUBJECT_TO_CHANGE
#include <polkitagent/polkitagent.h>
#include <glib-unix.h>
#include <stdio.h>
#include <unistd.h>
#include <string.h>
#include <pwd.h>

typedef struct { PolkitAgentListener parent; } DaisyListener;
typedef struct { PolkitAgentListenerClass parent; } DaisyListenerClass;
G_DEFINE_TYPE(DaisyListener, daisy_listener, POLKIT_AGENT_TYPE_LISTENER)
static GMainLoop *loop;
static PolkitAgentSession *session;
static GTask *pending;
static gulong cancellation;
static GCancellable *current_cancel;
static char *cookie;
static GList *allowed;
static gboolean waiting_identity;

static void emit(const char *kind, const char *text) {
    gchar *encoded = g_base64_encode((const guchar *)(text ? text : ""), strlen(text ? text : ""));
    g_print("%s\t%s\n", kind, encoded); fflush(stdout); g_free(encoded);
}
static void release_request(void) {
    if (cancellation) g_signal_handler_disconnect(current_cancel, cancellation);
    cancellation = 0; g_clear_object(&current_cancel);
    g_clear_object(&session); g_clear_pointer(&cookie, g_free);
    g_list_free_full(allowed, g_object_unref); allowed = NULL; waiting_identity = FALSE;
}
static void completed(PolkitAgentSession *source, gboolean authorized, gpointer data) {
    (void)source; (void)data;
    emit(authorized ? "success" : "failed", "");
    GTask *task = pending; pending = NULL;
    release_request();
    /* A failed password is an authentication result, not a successful authorization. */
    g_task_return_boolean(task, TRUE); g_object_unref(task);
}
static void request(PolkitAgentSession *source, const char *prompt, gboolean echo, gpointer data) {
    (void)source; (void)data; emit(echo ? "echo" : "password", prompt);
}
static void info(PolkitAgentSession *source, const char *text, gpointer data) { (void)source; (void)data; emit("info", text); }
static void error_message(PolkitAgentSession *source, const char *text, gpointer data) { (void)source; (void)data; emit("error", text); }
static void cancel_request(void) {
    if (session) { polkit_agent_session_cancel(session); return; }
    if (pending) {
        GTask *task = pending; pending = NULL; release_request();
        g_task_return_new_error(task, POLKIT_ERROR, POLKIT_ERROR_CANCELLED, "Authentication cancelled"); g_object_unref(task);
        emit("cancelled", "");
    }
}
static void cancelled(GCancellable *source, gpointer data) { (void)source; (void)data; cancel_request(); }
static void begin(PolkitAgentListener *listener, const gchar *action, const gchar *message, const gchar *icon,
                  PolkitDetails *details, const gchar *auth_cookie, GList *identities, GCancellable *stop,
                  GAsyncReadyCallback callback, gpointer data) {
    (void)icon;
    GTask *task = g_task_new(listener, stop, callback, data);
    if (pending) { g_task_return_new_error(task, POLKIT_ERROR, POLKIT_ERROR_FAILED, "Authentication is busy"); g_object_unref(task); return; }
    pending = task; cookie = g_strdup(auth_cookie);
    emit("begin", message); emit("action", action);
    const char *program = details ? polkit_details_lookup(details, "program") : NULL;
    if (program) emit("program", program);
    for (GList *item = identities; item; item = item->next) {
        if (!POLKIT_IS_UNIX_USER(item->data)) continue;
        allowed = g_list_append(allowed, g_object_ref(item->data));
        uid_t uid = polkit_unix_user_get_uid(item->data);
        struct passwd *user = getpwuid(uid);
        gchar *label = g_strdup_printf("%u:%s", (unsigned)uid, user ? user->pw_name : "User"); emit("identity", label); g_free(label);
    }
    waiting_identity = TRUE;
    if (stop) { current_cancel = g_object_ref(stop); cancellation = g_signal_connect(stop, "cancelled", G_CALLBACK(cancelled), NULL); }
    emit("ready", "");
    if (!allowed || (stop && g_cancellable_is_cancelled(stop))) cancel_request();
}
static gboolean finish(PolkitAgentListener *listener, GAsyncResult *res, GError **error) { (void)listener; return g_task_propagate_boolean(G_TASK(res), error); }
static void daisy_listener_class_init(DaisyListenerClass *klass) {
    PolkitAgentListenerClass *parent = POLKIT_AGENT_LISTENER_CLASS(klass);
    parent->initiate_authentication = begin; parent->initiate_authentication_finish = finish;
}
static void daisy_listener_init(DaisyListener *self) { (void)self; }
static gboolean input(GIOChannel *channel, GIOCondition condition, gpointer data) {
    (void)data;
    if (condition & (G_IO_HUP | G_IO_ERR)) { cancel_request(); g_main_loop_quit(loop); return G_SOURCE_REMOVE; }
    gchar *line = NULL;
    if (g_io_channel_read_line(channel, &line, NULL, NULL, NULL) != G_IO_STATUS_NORMAL) return G_SOURCE_CONTINUE;
    g_strchomp(line);
    gsize encoded_length = strlen(line);
    char *tab = strchr(line, '\t'); if (tab) *tab++ = 0;
    gsize length = 0; guchar *decoded = tab ? g_base64_decode(tab, &length) : NULL;
    gchar *text = decoded ? g_strndup((char *)decoded, length) : g_strdup("");
    if (!strcmp(line, "cancel")) cancel_request();
    else if (!strcmp(line, "identity") && waiting_identity) {
        for (GList *item = allowed; item; item = item->next) {
            gchar *uid = g_strdup_printf("%u", (unsigned)polkit_unix_user_get_uid(item->data));
            gboolean selected = !strcmp(uid, text); g_free(uid); if (!selected) continue;
            waiting_identity = FALSE; session = polkit_agent_session_new(item->data, cookie);
            g_signal_connect(session, "request", G_CALLBACK(request), NULL);
            g_signal_connect(session, "show-info", G_CALLBACK(info), NULL);
            g_signal_connect(session, "show-error", G_CALLBACK(error_message), NULL);
            g_signal_connect(session, "completed", G_CALLBACK(completed), NULL);
            polkit_agent_session_initiate(session); break;
        }
    } else if (!strcmp(line, "response") && session) polkit_agent_session_response(session, text);
    if (decoded) { memset(decoded, 0, length); g_free(decoded); }
    memset(text, 0, strlen(text)); g_free(text); memset(line, 0, encoded_length); g_free(line); return G_SOURCE_CONTINUE;
}
static PolkitSubject *session_subject(GError **error) {
    const char *id = g_getenv("XDG_SESSION_ID");
    if (id && *id) return polkit_unix_session_new(id);
    /* User services run outside the login scope. Find this user's active seat session,
       never the systemd user-manager pseudo-session. PolicyKit validates ownership. */
    GDBusConnection *system = g_bus_get_sync(G_BUS_TYPE_SYSTEM, NULL, error);
    if (!system) return NULL;
    GVariant *reply = g_dbus_connection_call_sync(system, "org.freedesktop.login1", "/org/freedesktop/login1",
        "org.freedesktop.login1.Manager", "ListSessions", NULL, G_VARIANT_TYPE("(a(susso))"), G_DBUS_CALL_FLAGS_NONE, 5000, NULL, error);
    PolkitSubject *subject = NULL;
    if (reply) {
        GVariantIter *items; const gchar *session_id, *user, *seat, *path; guint uid;
        g_variant_get(reply, "(a(susso))", &items);
        while (g_variant_iter_loop(items, "(&su&s&s&o)", &session_id, &uid, &user, &seat, &path)) {
            (void)user;
            if (uid != getuid() || !*seat) continue;
            GVariant *active = g_dbus_connection_call_sync(system, "org.freedesktop.login1", path,
                "org.freedesktop.DBus.Properties", "Get", g_variant_new("(ss)", "org.freedesktop.login1.Session", "Active"),
                G_VARIANT_TYPE("(v)"), G_DBUS_CALL_FLAGS_NONE, 5000, NULL, NULL);
            if (!active) continue;
            GVariant *value; g_variant_get(active, "(v)", &value);
            gboolean selected = g_variant_get_boolean(value); g_variant_unref(value); g_variant_unref(active);
            if (selected) { subject = polkit_unix_session_new(session_id); break; }
        }
        g_variant_iter_free(items); g_variant_unref(reply);
    }
    g_object_unref(system);
    if (!subject && (!error || !*error)) g_set_error(error, POLKIT_ERROR, POLKIT_ERROR_FAILED, "No active local login session");
    return subject;
}
int main(void) {
    GError *error = NULL;
    PolkitSubject *subject = session_subject(&error);
    if (!subject) { g_printerr("Session lookup failed: %s\n", error->message); return 1; }
    DaisyListener *listener = g_object_new(daisy_listener_get_type(), NULL);
    gpointer registration = polkit_agent_listener_register(POLKIT_AGENT_LISTENER(listener), POLKIT_AGENT_REGISTER_FLAGS_NONE,
        subject, "/org/daisyos/PolicyKit/AuthenticationAgent", NULL, &error);
    if (!registration) { g_printerr("Agent registration failed: %s\n", error->message); return 1; }
    loop = g_main_loop_new(NULL, FALSE);
    GIOChannel *stdin_channel = g_io_channel_unix_new(STDIN_FILENO);
    g_io_add_watch(stdin_channel, G_IO_IN | G_IO_HUP | G_IO_ERR, input, NULL);
    emit("registered", ""); g_main_loop_run(loop);
    polkit_agent_listener_unregister(registration); g_io_channel_unref(stdin_channel);
    g_object_unref(listener); g_object_unref(subject); g_main_loop_unref(loop); return 0;
}
