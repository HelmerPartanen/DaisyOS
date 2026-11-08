//! Headless session state for ScreenCast, RemoteDesktop, InputCapture, and GlobalShortcuts.
//! Interactive consent dialogs and sharing indicators delegate on-demand to the Avalonia UI worker,
//! while session creation, device/source selection, Continuity grants, and EIS/zone/barrier
//! operations for Continuity sessions run in the resident Rust broker without loading Avalonia.

use serde_json::{json, Value as JsonValue};
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
    process::Stdio,
    sync::{Arc, Mutex},
    time::Duration,
};
use tokio::{
    io::{AsyncBufReadExt, BufReader},
    process::{Child, Command},
};
use zbus::{
    export::futures_core::Stream,
    zvariant::{ObjectPath, OwnedFd, OwnedObjectPath, OwnedValue, Value},
};

use crate::ui_worker::UiLease;

pub fn is_continuity_session(session: &str) -> bool {
    session
        .rsplit('/')
        .next()
        .is_some_and(|name| name.starts_with("continuity_"))
}

pub async fn validate_continuity_grant(connection: &zbus::Connection, session: &str) -> bool {
    let Some(name) = session.rsplit('/').next() else {
        return false;
    };
    if !name.starts_with("continuity_") || name.len() != 75 {
        return false;
    }
    let token = &name[11..];
    let payload = json!({
        "action": "capture-grant",
        "id": "",
        "value": token,
        "allowed": false
    })
    .to_string();
    let res = tokio::time::timeout(
        Duration::from_secs(3),
        connection.call_method(
            Some("org.daisyos.Continuity1"),
            "/org/daisyos/Continuity1",
            Some("org.daisyos.Continuity1"),
            "Execute",
            &(payload.as_str(),),
        ),
    )
    .await;
    matches!(res, Ok(Ok(_)))
}

pub fn capture_helper_path(base_dir: &Path) -> PathBuf {
    let home = std::env::var_os("HOME")
        .map(PathBuf::from)
        .unwrap_or_default();
    let candidates = [
        home.join(".local/lib/daisyos/continuity/daisyos-portal-capture"),
        base_dir.join("daisyos-portal-capture"),
        PathBuf::from("/usr/lib/daisyos/apps/Continuity/daisyos-portal-capture"),
    ];
    for c in &candidates {
        if c.exists() {
            return c.clone();
        }
    }
    candidates[1].clone()
}

pub struct ScreenCastSession {
    pub app: String,
    pub types: u32,
    pub cursor: u32,
    pub multiple: bool,
    pub selected: bool,
    pub started: bool,
    pub remote: bool,
    pub continuity: bool,
    pub devices: u32,
    pub eis_connections: Vec<i32>,
    pub streams: Vec<Child>,
    pub delegated_to_ui: bool,
    pub ui_lease: Option<UiLease>,
}

pub struct InputCaptureSession {
    pub app: String,
    pub capabilities: u32,
    pub continuity: bool,
    pub native_path: Option<String>,
    pub zone_set: u32,
    pub activation: u32,
    pub zones: Vec<(u32, u32, i32, i32)>,
    pub barriers: Vec<(u32, i32, i32, i32, i32)>,
    pub stop_tx: Option<tokio::sync::oneshot::Sender<()>>,
    pub delegated_to_ui: bool,
    pub ui_lease: Option<UiLease>,
}

pub struct GlobalShortcutsSession {
    pub app: String,
    pub delegated_to_ui: bool,
    pub ui_lease: Option<UiLease>,
}

#[derive(Clone)]
pub struct SessionManager {
    pub screencast: Arc<Mutex<HashMap<String, ScreenCastSession>>>,
    pub input_capture: Arc<Mutex<HashMap<String, InputCaptureSession>>>,
    pub shortcuts: Arc<Mutex<HashMap<String, GlobalShortcutsSession>>>,
    pub base_dir: PathBuf,
}

impl SessionManager {
    pub fn new(base_dir: PathBuf) -> Self {
        Self {
            screencast: Arc::new(Mutex::new(HashMap::new())),
            input_capture: Arc::new(Mutex::new(HashMap::new())),
            shortcuts: Arc::new(Mutex::new(HashMap::new())),
            base_dir,
        }
    }

    pub async fn close_all(&self, connection: &zbus::Connection) {
        let sc_paths: Vec<String> = self.screencast.lock().unwrap().keys().cloned().collect();
        for p in sc_paths {
            self.close_screencast(connection, &p, false).await;
        }
        let ic_paths: Vec<String> = self.input_capture.lock().unwrap().keys().cloned().collect();
        for p in ic_paths {
            self.close_input_capture(connection, &p, false).await;
        }
        self.shortcuts.lock().unwrap().clear();
    }

    pub async fn close_session(&self, connection: &zbus::Connection, path: &str) -> bool {
        let mut was_delegated = false;
        if let Some(s) = self.screencast.lock().unwrap().get(path) {
            if s.delegated_to_ui {
                was_delegated = true;
            }
        }
        if let Some(s) = self.input_capture.lock().unwrap().get(path) {
            if s.delegated_to_ui {
                was_delegated = true;
            }
        }
        if let Some(s) = self.shortcuts.lock().unwrap().get(path) {
            if s.delegated_to_ui {
                was_delegated = true;
            }
        }
        self.close_screencast(connection, path, false).await;
        self.close_input_capture(connection, path, false).await;
        self.shortcuts.lock().unwrap().remove(path);
        was_delegated
    }

    pub async fn close_screencast(
        &self,
        connection: &zbus::Connection,
        path: &str,
        emit_signal: bool,
    ) {
        let removed = self.screencast.lock().unwrap().remove(path);
        let Some(mut session) = removed else {
            return;
        };
        for id in session.eis_connections {
            let conn = connection.clone();
            tokio::spawn(async move {
                let _ = conn
                    .call_method(
                        Some("org.kde.KWin"),
                        "/org/kde/KWin/EIS/RemoteDesktop",
                        Some("org.kde.KWin.EIS.RemoteDesktop"),
                        "disconnect",
                        &(id,),
                    )
                    .await;
            });
        }
        for mut child in session.streams.drain(..) {
            let _ = child.kill().await;
        }
        drop(session.ui_lease.take());
        if emit_signal {
            if let Ok(obj_path) = ObjectPath::try_from(path) {
                let _ = connection
                    .emit_signal(
                        None::<&str>,
                        obj_path,
                        "org.freedesktop.impl.portal.Session",
                        "Closed",
                        &(),
                    )
                    .await;
            }
        }
    }

    pub async fn close_input_capture(
        &self,
        connection: &zbus::Connection,
        path: &str,
        emit_signal: bool,
    ) {
        let removed = self.input_capture.lock().unwrap().remove(path);
        let Some(mut session) = removed else {
            return;
        };
        if let Some(tx) = session.stop_tx.take() {
            let _ = tx.send(());
        }
        if let Some(native) = session.native_path.take() {
            if let Ok(obj) = ObjectPath::try_from(native.as_str()) {
                let conn = connection.clone();
                let owned = OwnedObjectPath::from(obj);
                tokio::spawn(async move {
                    let _ = conn
                        .call_method(
                            Some("org.kde.KWin"),
                            "/org/kde/KWin/EIS/InputCapture",
                            Some("org.kde.KWin.EIS.InputCaptureManager"),
                            "removeInputCapture",
                            &(owned,),
                        )
                        .await;
                });
            }
        }
        drop(session.ui_lease.take());
        if emit_signal {
            if let Ok(obj_path) = ObjectPath::try_from(path) {
                let _ = connection
                    .emit_signal(
                        None::<&str>,
                        obj_path,
                        "org.freedesktop.impl.portal.Session",
                        "Closed",
                        &(),
                    )
                    .await;
            }
        }
    }

    pub async fn start_continuity_screencast(
        &self,
        connection: &zbus::Connection,
        session_path: &str,
        types: u32,
        cursor: u32,
        remote: bool,
        devices: u32,
    ) -> Result<(u32, HashMap<String, OwnedValue>), zbus::Error> {
        let sources = self.list_sources(connection, types).await?;
        let Some(first) = sources.first() else {
            return Ok((2, HashMap::new()));
        };
        let helper = capture_helper_path(&self.base_dir);
        let mut child = Command::new(&helper)
            .arg(if first.window { "--window" } else { "--output" })
            .arg(&first.id)
            .arg(cursor.to_string())
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true)
            .spawn()
            .map_err(|e| zbus::Error::Failure(e.to_string()))?;

        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| zbus::Error::Failure("No stdout".into()))?;
        let mut lines = BufReader::new(stdout).lines();
        let line = tokio::time::timeout(Duration::from_secs(10), lines.next_line())
            .await
            .map_err(|_| zbus::Error::Failure("Capture timeout".into()))?
            .map_err(|e| zbus::Error::Failure(e.to_string()))?
            .ok_or_else(|| zbus::Error::Failure("Capture closed".into()))?;

        let Some(node_str) = line.strip_prefix("node ") else {
            let _ = child.kill().await;
            return Ok((2, HashMap::new()));
        };
        let Ok(node) = node_str.trim().parse::<u32>() else {
            let _ = child.kill().await;
            return Ok((2, HashMap::new()));
        };

        let mut props: HashMap<String, OwnedValue> = HashMap::new();
        props.insert(
            "source_type".into(),
            OwnedValue::from(if first.window { 2u32 } else { 1u32 }),
        );
        if !first.window {
            props.insert(
                "position".into(),
                OwnedValue::try_from(Value::from((first.x, first.y))).unwrap(),
            );
            props.insert(
                "size".into(),
                OwnedValue::try_from(Value::from((first.width, first.height))).unwrap(),
            );
        }

        let streams = vec![(node, props)];
        {
            let mut guard = self.screencast.lock().unwrap();
            let Some(session) = guard.get_mut(session_path) else {
                let _ = child.kill().await;
                return Ok((2, HashMap::new()));
            };
            session.started = true;
            session.streams.push(child);
        }

        let mut results: HashMap<String, OwnedValue> = HashMap::new();
        results.insert(
            "streams".into(),
            OwnedValue::try_from(Value::from(streams)).unwrap(),
        );
        if remote {
            results.insert("devices".into(), OwnedValue::from(devices));
        }
        Ok((0, results))
    }

    async fn list_sources(
        &self,
        connection: &zbus::Connection,
        types: u32,
    ) -> Result<Vec<CaptureSource>, zbus::Error> {
        let mut sources = Vec::new();
        if (types & 1) != 0 {
            let helper = capture_helper_path(&self.base_dir);
            let output = Command::new(&helper)
                .arg("--list")
                .stdout(Stdio::piped())
                .stderr(Stdio::piped())
                .output()
                .await
                .map_err(|e| zbus::Error::Failure(e.to_string()))?;
            if !output.status.success() {
                return Err(zbus::Error::Failure(
                    "Compositor capture capability is unavailable.".into(),
                ));
            }
            let text = String::from_utf8_lossy(&output.stdout);
            for line in text.lines() {
                let parts: Vec<&str> = line.split('\t').collect();
                if parts.len() == 6 && parts[0] == "output" {
                    if let (Ok(x), Ok(y), Ok(w), Ok(h)) = (
                        parts[2].parse::<i32>(),
                        parts[3].parse::<i32>(),
                        parts[4].parse::<i32>(),
                        parts[5].parse::<i32>(),
                    ) {
                        sources.push(CaptureSource {
                            id: parts[1].to_string(),
                            window: false,
                            x,
                            y,
                            width: w,
                            height: h,
                        });
                    }
                }
            }
        }
        if (types & 2) != 0 {
            if let Ok(reply) = tokio::time::timeout(
                Duration::from_secs(3),
                connection.call_method(
                    Some("org.daisyos.Shell.WindowBridge"),
                    "/org/daisyos/Shell/WindowBridge",
                    Some("org.daisyos.Shell.WindowBridge"),
                    "GetCaptureWindows",
                    &(),
                ),
            )
            .await
            {
                if let Ok(msg) = reply {
                    if let Ok(json_str) = msg.body().deserialize::<String>() {
                        if let Ok(doc) = serde_json::from_str::<JsonValue>(&json_str) {
                            if let Some(arr) = doc.get("windows").and_then(JsonValue::as_array) {
                                for item in arr {
                                    let id = item.get("id").and_then(JsonValue::as_str).unwrap_or("");
                                    let app_id =
                                        item.get("appId").and_then(JsonValue::as_str).unwrap_or("");
                                    if !id.is_empty()
                                        && !app_id.is_empty()
                                        && app_id != "org.daisyos.DesktopServices"
                                    {
                                        sources.push(CaptureSource {
                                            id: id.to_string(),
                                            window: true,
                                            x: 0,
                                            y: 0,
                                            width: 0,
                                            height: 0,
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        Ok(sources)
    }

    pub async fn watch_input_capture_signals(
        &self,
        connection: zbus::Connection,
        session_path: String,
        native_path: String,
        mut stop_rx: tokio::sync::oneshot::Receiver<()>,
    ) {
        let Ok(rule) = zbus::MatchRule::builder()
            .msg_type(zbus::message::Type::Signal)
            .sender("org.kde.KWin")
            .and_then(|b| b.path(native_path.as_str()))
            .and_then(|b| b.interface("org.kde.KWin.EIS.InputCapture"))
            .map(|b| b.build())
        else {
            return;
        };
        let Ok(mut stream) = zbus::MessageStream::for_match_rule(rule, &connection, None).await
        else {
            return;
        };
        let sessions = self.input_capture.clone();
        tokio::spawn(async move {
            use std::future::poll_fn;
            loop {
                tokio::select! {
                    _ = &mut stop_rx => break,
                    next = poll_fn(|cx| std::pin::Pin::new(&mut stream).poll_next(cx)) => {
                        let Some(Ok(msg)) = next else { break };
                        let header = msg.header();
                        let Some(member) = header.member().map(|m| m.as_str()) else { continue };
                        let mut options: HashMap<String, OwnedValue> = HashMap::new();
                        let signal_name = match member {
                            "disabled" => "Disabled",
                            "deactivated" => {
                                let Ok(activation) = msg.body().deserialize::<u32>() else { continue };
                                if let Some(s) = sessions.lock().unwrap().get_mut(&session_path) {
                                    s.activation = activation;
                                }
                                options.insert("activation_id".into(), OwnedValue::from(activation));
                                "Deactivated"
                            }
                            "activated" => {
                                let Ok((activation, barrier, (x, y))) = msg.body().deserialize::<(u32, u32, (f64, f64))>() else { continue };
                                if let Some(s) = sessions.lock().unwrap().get_mut(&session_path) {
                                    s.activation = activation;
                                }
                                options.insert("activation_id".into(), OwnedValue::from(activation));
                                options.insert("barrier_id".into(), OwnedValue::from(barrier));
                                options.insert(
                                    "cursor_position".into(),
                                    OwnedValue::try_from(Value::from((x, y))).unwrap(),
                                );
                                "Activated"
                            }
                            _ => continue,
                        };
                        if let Ok(obj_path) = ObjectPath::try_from(session_path.as_str()) {
                            let _ = connection
                                .emit_signal(
                                    None::<&str>,
                                    "/org/freedesktop/portal/desktop",
                                    "org.freedesktop.impl.portal.InputCapture",
                                    signal_name,
                                    &(obj_path, options),
                                )
                                .await;
                        }
                    }
                }
            }
        });
    }

    pub async fn connect_remote_desktop_eis(
        &self,
        connection: &zbus::Connection,
        path: &str,
        app: &str,
    ) -> Result<OwnedFd, zbus::Error> {
        let devices = {
            let guard = self.screencast.lock().unwrap();
            let Some(session) = guard.get(path) else {
                return Err(zbus::fdo::Error::AccessDenied("Invalid session".into()).into());
            };
            if session.app != app || !session.started || !session.remote || session.devices == 0 {
                return Err(zbus::fdo::Error::AccessDenied("Not authorized".into()).into());
            }
            session.devices as i32
        };

        let reply = connection
            .call_method(
                Some("org.kde.KWin"),
                "/org/kde/KWin/EIS/RemoteDesktop",
                Some("org.kde.KWin.EIS.RemoteDesktop"),
                "connectToEIS",
                &(devices,),
            )
            .await?;

        let (fd, id): (OwnedFd, i32) = reply.body().deserialize()?;
        let still_exists = {
            let mut guard = self.screencast.lock().unwrap();
            if let Some(session) = guard.get_mut(path) {
                session.eis_connections.push(id);
                true
            } else {
                false
            }
        };
        if !still_exists {
            let _ = connection
                .call_method(
                    Some("org.kde.KWin"),
                    "/org/kde/KWin/EIS/RemoteDesktop",
                    Some("org.kde.KWin.EIS.RemoteDesktop"),
                    "disconnect",
                    &(id,),
                )
                .await;
            return Err(zbus::fdo::Error::AccessDenied("Session closed".into()).into());
        }
        Ok(fd)
    }

    pub async fn connect_input_capture_eis(
        &self,
        connection: &zbus::Connection,
        path: &str,
        app: &str,
    ) -> Result<OwnedFd, zbus::Error> {
        let native = {
            let guard = self.input_capture.lock().unwrap();
            let Some(session) = guard.get(path) else {
                return Err(zbus::fdo::Error::AccessDenied("Invalid session".into()).into());
            };
            if session.app != app {
                return Err(zbus::fdo::Error::AccessDenied("App mismatch".into()).into());
            }
            let Some(ref n) = session.native_path else {
                return Err(zbus::fdo::Error::AccessDenied("No native session".into()).into());
            };
            n.clone()
        };

        let reply = connection
            .call_method(
                Some("org.kde.KWin"),
                native.as_str(),
                Some("org.kde.KWin.EIS.InputCapture"),
                "connectToEIS",
                &(),
            )
            .await?;

        let fd: OwnedFd = reply.body().deserialize()?;
        if !self.input_capture.lock().unwrap().contains_key(path) {
            return Err(zbus::fdo::Error::AccessDenied("Session closed".into()).into());
        }
        Ok(fd)
    }
}

struct CaptureSource {
    id: String,
    window: bool,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
}
