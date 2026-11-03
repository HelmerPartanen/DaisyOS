/* WebKit renders mail in a disposable, ephemeral process. Only pixels and link
 * rectangles cross back into the native UI; documents cannot navigate the app. */
#include <webkit2/webkit2.h>
#include <stdio.h>
#include <stdlib.h>

static const char *png_path, *map_path;
static gboolean finishing;
static int status = 1;
static int raster_scale = 1;
static void snapshot_done(GObject *object, GAsyncResult *result, gpointer unused) {
    (void)unused;
    GError *error = NULL;
    cairo_surface_t *surface = webkit_web_view_get_snapshot_finish(WEBKIT_WEB_VIEW(object), result, &error);
    if (surface) {
        status = cairo_surface_write_to_png(surface, png_path) == CAIRO_STATUS_SUCCESS ? 0 : 1;
        cairo_surface_destroy(surface);
    }
    g_clear_error(&error);
    gtk_main_quit();
}
static void layout_done(GObject *object, GAsyncResult *result, gpointer unused) {
    (void)unused;
    GError *error = NULL;
    JSCValue *value = webkit_web_view_evaluate_javascript_finish(WEBKIT_WEB_VIEW(object), result, &error);
    if (!value) { g_clear_error(&error); gtk_main_quit(); return; }
    char *json = jsc_value_to_string(value);
    gboolean saved = g_file_set_contents(map_path, json, -1, &error);
    g_free(json); g_object_unref(value);
    if (!saved) { g_clear_error(&error); gtk_main_quit(); return; }
    webkit_web_view_get_snapshot(WEBKIT_WEB_VIEW(object), WEBKIT_SNAPSHOT_REGION_FULL_DOCUMENT,
        WEBKIT_SNAPSHOT_OPTIONS_NONE, NULL, snapshot_done, NULL);
}
static gboolean finish(gpointer object) {
    if (finishing) return G_SOURCE_REMOVE;
    finishing = TRUE;
    /* An isolated world can inspect layout; CSP blocks all document scripts.
     * Bound document dimensions before allocating the full-page snapshot. */
    char *script = g_strdup_printf( "(()=>{const w=Math.max(document.documentElement.scrollWidth,innerWidth),h=Math.max(document.documentElement.scrollHeight,document.body.scrollHeight);"
        "if(w>4096||h>30000||w*h>24000000||w*h*%d>64000000)throw Error('Message layout is too large');"
        "const links=[...document.querySelectorAll('a[href]')].slice(0,2000).flatMap(a=>[...a.getClientRects(),...[...a.querySelectorAll('img')].map(i=>i.getBoundingClientRect())].map(r=>({x:r.x+scrollX,y:r.y+scrollY,width:r.width,height:r.height,uri:a.getAttribute('href'),targetY:(()=>{const u=a.getAttribute('href');if(!u?.startsWith('#'))return null;try{const t=document.getElementById(decodeURIComponent(u.slice(1)));return t?t.getBoundingClientRect().y+scrollY:null}catch{return null}})(),label:(a.innerText||a.querySelector('img')?.alt||a.getAttribute('href')).slice(0,300)})));"
        "return JSON.stringify({width:w,height:h,links});})()", raster_scale * raster_scale);
    webkit_web_view_evaluate_javascript(WEBKIT_WEB_VIEW(object), script, -1, "daisy-mail-layout", NULL, NULL, layout_done, NULL);
    g_free(script);
    return G_SOURCE_REMOVE;
}
static void loaded(WebKitWebView *view, WebKitLoadEvent event, gpointer unused) {
    (void)unused;
    if (event == WEBKIT_LOAD_FINISHED) g_timeout_add(100, finish, view);
}
static gboolean timeout(gpointer unused) { (void)unused; gtk_main_quit(); return G_SOURCE_REMOVE; }
static gboolean policy(WebKitWebView *view, WebKitPolicyDecision *decision, WebKitPolicyDecisionType type, gpointer unused) {
    (void)view; (void)unused;
    if (type == WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION) { webkit_policy_decision_ignore(decision); return TRUE; }
    if (type == WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION) {
        WebKitURIRequest *request = webkit_navigation_policy_decision_get_request(WEBKIT_NAVIGATION_POLICY_DECISION(decision));
        const char *uri = webkit_uri_request_get_uri(request);
        if (g_strcmp0(uri, "about:blank") != 0) { webkit_policy_decision_ignore(decision); return TRUE; }
    }
    return FALSE;
}
int main(int argc, char **argv) {
    if (argc != 5 && argc != 6) return 2;
    int width = atoi(argv[4]);
    if (width < 240 || width > 1800) return 2;
    raster_scale = argc == 6 ? atoi(argv[5]) : 1;
    if (raster_scale < 1 || raster_scale > 3) return 2;
    png_path = argv[2]; map_path = argv[3];
    g_setenv("GDK_BACKEND", "x11", TRUE);
    g_setenv("GDK_SCALE", raster_scale == 3 ? "3" : raster_scale == 2 ? "2" : "1", TRUE);
    g_setenv("GDK_DPI_SCALE", "1", TRUE);
    if (!gtk_init_check(NULL, NULL)) return 3;
    g_object_set(gtk_settings_get_default(), "gtk-xft-dpi", 96 * 1024, NULL);
    gchar *html = NULL; gsize length = 0;
    if (!g_file_get_contents(argv[1], &html, &length, NULL) || length > 16*1024*1024) return 4;
    WebKitWebContext *context = webkit_web_context_new_ephemeral();
    WebKitWebView *view = WEBKIT_WEB_VIEW(webkit_web_view_new_with_context(context));
    WebKitSettings *settings = webkit_web_view_get_settings(view);
    webkit_settings_set_hardware_acceleration_policy(settings, WEBKIT_HARDWARE_ACCELERATION_POLICY_NEVER);
    webkit_settings_set_enable_html5_local_storage(settings, FALSE);
    webkit_settings_set_enable_html5_database(settings, FALSE);
    webkit_settings_set_enable_dns_prefetching(settings, FALSE);
    webkit_settings_set_enable_media(settings, FALSE);
    webkit_settings_set_allow_file_access_from_file_urls(settings, FALSE);
    webkit_settings_set_allow_universal_access_from_file_urls(settings, FALSE);
    webkit_settings_set_javascript_can_open_windows_automatically(settings, FALSE);
    GtkWidget *window = gtk_offscreen_window_new();
    gtk_widget_set_size_request(GTK_WIDGET(view), width, 1);
    gtk_container_add(GTK_CONTAINER(window), GTK_WIDGET(view));
    g_signal_connect(view, "load-changed", G_CALLBACK(loaded), NULL);
    g_signal_connect(view, "decide-policy", G_CALLBACK(policy), NULL);
    gtk_widget_show_all(window);
    webkit_web_view_load_html(view, html, "about:blank");
    g_free(html);
    g_timeout_add_seconds(12, finish, view); /* Slow image hosts cannot stall mail. */
    g_timeout_add_seconds(20, timeout, NULL);
    gtk_main();
    gtk_widget_destroy(window); g_object_unref(context);
    return status;
}
