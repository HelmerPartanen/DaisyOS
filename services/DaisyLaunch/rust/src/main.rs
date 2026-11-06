//! Resident request broker. KDE discovery and launch semantics live in the
//! on-demand sibling helper, so no GUI toolkit stays loaded between requests.
mod registry;
use serde_json::{json, Value};
use std::{
    collections::HashMap,
    path::PathBuf,
    sync::{
        atomic::{AtomicUsize, Ordering},
        Arc, Mutex,
    },
    time::{Duration, Instant},
};
use tokio::{io::AsyncWriteExt, process::Command, sync::watch};
use zbus::{interface, message::Header, object_server::SignalEmitter, zvariant::OwnedValue};

const PATH: &str = "/org/daisyos/Launcher1";
const IFACE: &str = "org.daisyos.Launcher1";
const REQUEST_BYTES: usize = 8 * 1024 * 1024;
const RETENTION: Duration = Duration::from_secs(120);
#[derive(Debug, zbus::DBusError)]
#[zbus(prefix = "org.daisyos.Launcher1")]
enum Error {
    InvalidArgument(String),
    NotFound(String),
    RateLimited(String),
    Cancelled(String),
    TimedOut(String),
    BackendFailed(String),
    AccessDenied(String),
    #[zbus(error)]
    ZBus(zbus::Error),
}
impl Error {
    fn result(code: &str) -> Self {
        let text = "Application launch did not complete".to_string();
        match code {
            "NotFound" => Self::NotFound(text),
            "InvalidArgument" => Self::InvalidArgument(text),
            "Cancelled" => Self::Cancelled(text),
            "TimedOut" => Self::TimedOut(text),
            _ => Self::BackendFailed(text),
        }
    }
}
fn now() -> String {
    let mut t = libc::timespec {
        tv_sec: 0,
        tv_nsec: 0,
    };
    // SAFETY: valid timespec, CLOCK_MONOTONIC is available on Linux.
    unsafe {
        libc::clock_gettime(libc::CLOCK_MONOTONIC, &mut t);
    }
    (t.tv_sec as u128 * 1_000_000_000 + t.tv_nsec as u128).to_string()
}
struct Request {
    owner: String,
    app: String,
    action: String,
    uris: Vec<String>,
    created: Instant,
    retained_bytes: usize,
    done: watch::Sender<Option<String>>,
    trace: Mutex<HashMap<&'static str, String>>,
    waiters: AtomicUsize,
}
#[derive(Clone)]
struct Launcher {
    requests: Arc<Mutex<HashMap<String, Arc<Request>>>>,
    catalog: Arc<Mutex<String>>,
    backend: PathBuf,
    telemetry: bool,
    helpers: Arc<tokio::sync::Semaphore>,
}
impl Launcher {
    fn stamp(&self, r: &Request, event: &'static str) {
        if self.telemetry {
            r.trace.lock().unwrap().insert(event, now());
        }
    }
    async fn authorize(
        &self,
        header: &Header<'_>,
        connection: &zbus::Connection,
    ) -> Result<String, Error> {
        let sender = header
            .sender()
            .ok_or_else(|| Error::AccessDenied("Missing caller".into()))?;
        let proxy = zbus::fdo::DBusProxy::new(connection).await?;
        let uid = proxy
            .get_connection_unix_user(sender.as_ref().into())
            .await
            .map_err(zbus::Error::from)?;
        // SAFETY: getuid has no arguments or memory preconditions.
        if uid != unsafe { libc::getuid() } {
            return Err(Error::AccessDenied("Caller is not the session user".into()));
        }
        Ok(sender.to_string())
    }
    fn lookup(&self, id: &str, owner: &str) -> Result<Arc<Request>, Error> {
        self.requests
            .lock()
            .unwrap()
            .get(id)
            .filter(|r| r.owner == owner)
            .cloned()
            .ok_or_else(|| Error::NotFound("Unknown request".into()))
    }
    async fn state(connection: &zbus::Connection, id: &str, state: &str) {
        let _ = connection
            .emit_signal(
                None::<&str>,
                PATH,
                IFACE,
                "LaunchStateChanged",
                &(id, state),
            )
            .await;
    }
    async fn finish(&self, connection: &zbus::Connection, id: &str, r: &Request, error: &str) {
        // All request completion runs on one Tokio thread, with no await between
        // this check and send_replace. The first terminal result wins.
        if r.done.borrow().is_some() {
            return;
        }
        if error.is_empty() {
            self.stamp(r, "dispatch_accepted");
        }
        r.done.send_replace(Some(error.to_string()));
        Self::state(
            connection,
            id,
            if error.is_empty() { "Accepted" } else { error },
        )
        .await;
        if !error.is_empty() {
            let _ = connection
                .emit_signal(
                    None::<&str>,
                    PATH,
                    IFACE,
                    "LaunchFailed",
                    &(id, error, "Application launch did not complete"),
                )
                .await;
        }
    }
    async fn execute(&self, connection: &zbus::Connection, r: &Request, token: &str) -> String {
        // A request burst must not load one GUI toolkit per pending request.
        // Queue behind a single on-demand helper; cancelled queued work never spawns.
        let Ok(permit) = self.helpers.acquire().await else {
            return "BackendFailed".into();
        };
        if r.done.borrow().is_some() {
            return "Cancelled".into();
        }
        self.stamp(r, "backend_started");
        let mut command = Command::new(&self.backend);
        command
            .arg("--broker-launch")
            .stdin(std::process::Stdio::piped())
            .stdout(std::process::Stdio::piped())
            .stderr(std::process::Stdio::null())
            .kill_on_drop(true);
        let Ok(mut child) = command.spawn() else {
            return "BackendFailed".into();
        };
        let payload =
            json!({"app": r.app, "action": r.action, "uris": r.uris, "token": token}).to_string();
        let mut input = child.stdin.take().unwrap();
        if input.write_all(payload.as_bytes()).await.is_err() {
            return "BackendFailed".into();
        }
        drop(input);
        drop(payload);
        let output = child.wait_with_output().await;
        drop(permit);
        let Ok(output) = output else {
            return "BackendFailed".into();
        };
        if output.stdout.len() > 4096 {
            return "BackendFailed".into();
        }
        let Ok(value) = serde_json::from_slice::<Value>(&output.stdout) else {
            return "BackendFailed".into();
        };
        if self.telemetry {
            for key in [
                "registry_lookup_started",
                "registry_lookup_finished",
                "dispatch_started",
            ] {
                if let Some(stamp) = value
                    .get("trace")
                    .and_then(|v| v.get(key))
                    .and_then(Value::as_str)
                {
                    if !stamp.is_empty()
                        && stamp.bytes().all(|b| b.is_ascii_digit())
                        && stamp.len() <= 32
                    {
                        r.trace.lock().unwrap().insert(key, stamp.to_owned());
                    }
                }
            }
        }
        if let Some(activation) = value.get("activation").filter(|_| output.status.success()) {
            let (Some(service), Some(path)) = (
                activation.get("service").and_then(Value::as_str),
                activation.get("path").and_then(Value::as_str),
            ) else {
                return "BackendFailed".into();
            };
            let mut data = HashMap::<&str, zbus::zvariant::Value<'_>>::new();
            if !token.is_empty() {
                data.insert("activation-token", zbus::zvariant::Value::from(token));
            }
            // KService validated the desktop action and resolved the authoritative
            // service/path. Rust waits for the asynchronous app reply without
            // retaining a KDE helper or occupying the helper semaphore.
            let result = if !r.action.is_empty() {
                connection
                    .call_method(
                        Some(service),
                        path,
                        Some("org.freedesktop.Application"),
                        "ActivateAction",
                        &(
                            r.action.as_str(),
                            Vec::<zbus::zvariant::Value<'_>>::new(),
                            data,
                        ),
                    )
                    .await
            } else if !r.uris.is_empty() {
                connection
                    .call_method(
                        Some(service),
                        path,
                        Some("org.freedesktop.Application"),
                        "Open",
                        &(&r.uris, data),
                    )
                    .await
            } else {
                connection
                    .call_method(
                        Some(service),
                        path,
                        Some("org.freedesktop.Application"),
                        "Activate",
                        &(data,),
                    )
                    .await
            };
            return if result.is_ok() {
                String::new()
            } else {
                "BackendFailed".into()
            };
        }
        value
            .get("error")
            .and_then(Value::as_str)
            .filter(|code| {
                [
                    "",
                    "NotFound",
                    "InvalidArgument",
                    "BackendFailed",
                    "TimedOut",
                ]
                .contains(code)
            })
            .filter(|code| !code.is_empty() || output.status.success())
            .unwrap_or("BackendFailed")
            .to_owned()
    }
    async fn dispatch(
        self,
        connection: zbus::Connection,
        id: String,
        r: Arc<Request>,
        token: String,
    ) {
        let mut completion = r.done.subscribe();
        if completion.borrow().is_some() {
            return;
        }
        Self::state(&connection, &id, "Resolving").await;
        Self::state(&connection, &id, "Dispatching").await;
        if completion.borrow().is_some() {
            return;
        }
        tokio::select! {
            error = self.execute(&connection, &r, &token) => { self.finish(&connection, &id, &r, &error).await; },
            _ = completion.changed() => {},
            _ = tokio::time::sleep(Duration::from_secs(15)) => { self.finish(&connection, &id, &r, "TimedOut").await; },
        }
    }
}
#[interface(name = "org.daisyos.Launcher1")]
impl Launcher {
    #[zbus(property)]
    fn api_version(&self) -> u32 {
        1
    }
    async fn catalog(
        &self,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<String, Error> {
        self.authorize(&header, connection).await?;
        Ok(self.catalog.lock().unwrap().clone())
    }
    #[allow(clippy::too_many_arguments)]
    async fn launch(
        &self,
        request_id: String,
        desktop_id: String,
        action_id: String,
        uris: Vec<String>,
        token: String,
        options: HashMap<String, OwnedValue>,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<bool, Error> {
        let received = now();
        let owner = self.authorize(&header, connection).await?;
        if request_id.len() != 32
            || !request_id
                .bytes()
                .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
            || if desktop_id.starts_with('/') {
                desktop_id.len() > 4096 || !desktop_id.ends_with(".desktop")
            } else {
                desktop_id.len() > 256 || desktop_id.contains('/')
            }
            || action_id.len() > 128
            || token.len() > 4096
            || uris.len() > 64
            || !options.is_empty()
            || uris.iter().any(|u| !valid_uri(u))
        {
            return Err(Error::InvalidArgument("Invalid request".into()));
        }
        let retained_bytes = desktop_id.len()
            + action_id.len()
            + token.len()
            + uris.iter().map(String::len).sum::<usize>();
        let request = {
            let mut requests = self.requests.lock().unwrap();
            requests.retain(|_, r| r.created.elapsed() < RETENTION || r.done.borrow().is_none());
            if let Some(r) = requests.get(&request_id) {
                return if r.owner == owner
                    && r.app == desktop_id
                    && r.action == action_id
                    && r.uris == uris
                {
                    Ok(true)
                } else {
                    Err(Error::InvalidArgument("Request ID conflict".into()))
                };
            }
            let owned: Vec<_> = requests.values().filter(|r| r.owner == owner).collect();
            if requests.len() >= 1024
                || requests.values().map(|r| r.retained_bytes).sum::<usize>() + retained_bytes
                    > REQUEST_BYTES
                || owned.iter().filter(|r| r.done.borrow().is_none()).count() >= 16
                || owned
                    .iter()
                    .filter(|r| r.created.elapsed() < Duration::from_secs(1))
                    .count()
                    >= 20
            {
                return Err(Error::RateLimited("Too many requests".into()));
            }
            let (done, _) = watch::channel(None);
            let r = Arc::new(Request {
                owner,
                app: desktop_id,
                action: action_id,
                uris,
                created: Instant::now(),
                retained_bytes,
                done,
                trace: Mutex::new(HashMap::new()),
                waiters: AtomicUsize::new(0),
            });
            if self.telemetry {
                r.trace.lock().unwrap().insert("daemon_received", received);
            }
            requests.insert(request_id.clone(), r.clone());
            r
        };
        Self::state(connection, &request_id, "Requested").await;
        tokio::spawn(
            self.clone()
                .dispatch(connection.clone(), request_id, request, token),
        );
        Ok(true)
    }
    #[zbus(name = "Await")]
    async fn await_request(
        &self,
        id: String,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<bool, Error> {
        let owner = self.authorize(&header, connection).await?;
        let request = self.lookup(&id, &owner)?;
        let mut receiver = request.done.subscribe();
        if receiver.borrow().is_none() && request.waiters.load(Ordering::Relaxed) >= 4 {
            return Err(Error::RateLimited("Too many waiters".into()));
        }
        struct Waiter<'a>(&'a AtomicUsize);
        impl Drop for Waiter<'_> {
            fn drop(&mut self) {
                self.0.fetch_sub(1, Ordering::Relaxed);
            }
        }
        request.waiters.fetch_add(1, Ordering::Relaxed);
        let _waiter = Waiter(&request.waiters);
        loop {
            if let Some(error) = receiver.borrow().as_ref() {
                return if error.is_empty() {
                    Ok(true)
                } else {
                    Err(Error::result(error))
                };
            }
            if receiver.changed().await.is_err() {
                return Err(Error::BackendFailed("Request lost".into()));
            }
        }
    }
    async fn cancel(
        &self,
        id: String,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<(), Error> {
        let owner = self.authorize(&header, connection).await?;
        if let Ok(r) = self.lookup(&id, &owner) {
            self.finish(connection, &id, &r, "Cancelled").await;
        }
        Ok(())
    }
    async fn trace(
        &self,
        id: String,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<String, Error> {
        let owner = self.authorize(&header, connection).await?;
        let Ok(r) = self.lookup(&id, &owner) else {
            return Ok(String::new());
        };
        if !self.telemetry {
            return Ok("{}".into());
        }
        let mut value = json!({"activation_ready": null, "first_committed": null, "first_presented": null, "ready": null,
            "daemon_received": null, "registry_lookup_started": null, "registry_lookup_finished": null,
            "dispatch_started": null, "dispatch_accepted": null, "surface_associated": null});
        for (key, stamp) in r.trace.lock().unwrap().iter() {
            value[*key] = json!(stamp);
        }
        Ok(value.to_string())
    }
    async fn associate(
        &self,
        id: String,
        window: String,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<(), Error> {
        let owner = self.authorize(&header, connection).await?;
        if let Ok(r) = self.lookup(&id, &owner) {
            if window.len() < 256
                && r.done
                    .borrow()
                    .as_ref()
                    .is_none_or(|error| error.is_empty())
            {
                self.stamp(&r, "surface_associated");
                Self::state(connection, &id, "WindowIdentified").await;
            }
        }
        Ok(())
    }
    #[zbus(signal)]
    async fn launch_state_changed(
        emitter: &SignalEmitter<'_>,
        request_id: &str,
        state: &str,
    ) -> zbus::Result<()>;
    #[zbus(signal)]
    async fn launch_failed(
        emitter: &SignalEmitter<'_>,
        request_id: &str,
        error_code: &str,
        message: &str,
    ) -> zbus::Result<()>;
    #[zbus(signal)]
    async fn registry_changed(emitter: &SignalEmitter<'_>) -> zbus::Result<()>;
}
fn valid_uri(uri: &str) -> bool {
    if uri.len() > 8192 || uri.bytes().any(|b| b < 32 || b == 127) {
        return false;
    }
    let Some((scheme, _)) = uri.split_once(':') else {
        return false;
    };
    !scheme.is_empty()
        && scheme.as_bytes()[0].is_ascii_alphabetic()
        && scheme
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b"+.-".contains(&b))
}
fn backend() -> std::io::Result<PathBuf> {
    Ok(std::env::current_exe()?.with_file_name("daisy-launch-backend"))
}
#[tokio::main(flavor = "current_thread")]
async fn main() -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let backend = backend()?;
    if std::env::args_os().len() > 1 {
        use std::os::unix::process::CommandExt;
        return Err(std::process::Command::new(&backend)
            .args(std::env::args_os().skip(1))
            .exec()
            .into());
    }
    let catalog = registry::catalog(&backend).await?;
    let launcher = Launcher {
        requests: Arc::new(Mutex::new(HashMap::new())),
        catalog: Arc::new(Mutex::new(catalog)),
        backend,
        helpers: Arc::new(tokio::sync::Semaphore::new(1)),
        telemetry: std::env::var("DAISYOS_LAUNCH_TELEMETRY").as_deref() != Ok("0"),
    };
    let connection = zbus::connection::Builder::session()?
        .serve_at(PATH, launcher.clone())?
        .name(IFACE)?
        .build()
        .await?;
    tokio::spawn(registry::watch(launcher.clone(), connection.clone()));
    let requests = launcher.requests.clone();
    tokio::spawn(async move {
        let mut tick = tokio::time::interval(Duration::from_secs(10));
        loop {
            tick.tick().await;
            requests
                .lock()
                .unwrap()
                .retain(|_, r| r.created.elapsed() < RETENTION || r.done.borrow().is_none());
        }
    });
    // Also exit if the bus disappears; the service manager can reconnect us.
    let mut messages = zbus::MessageStream::from(&connection);
    use std::future::poll_fn;
    use zbus::export::futures_core::Stream;
    let mut terminate = tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())?;
    tokio::select! {
        _ = terminate.recv() => {},
        _ = tokio::signal::ctrl_c() => {},
        _ = async { while poll_fn(|cx| std::pin::Pin::new(&mut messages).poll_next(cx)).await.is_some() {} } => {},
    }
    Ok(())
}
