use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{
    collections::HashSet,
    fs,
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
    time::Duration,
};
use tokio::{
    process::{Child, Command},
    time,
};
use zbus::{interface, message::Header};

const BUS: &str = "org.daisyos.Gaming1";
const PATH: &str = "/org/daisyos/Gaming1";
const PROFILE_POLL: Duration = Duration::from_secs(3);

#[derive(Debug, zbus::DBusError)]
#[zbus(prefix = "org.daisyos.Gaming1")]
enum Error {
    AccessDenied(String),
    InvalidArgument(String),
    #[zbus(error)]
    ZBus(zbus::Error),
}

#[derive(Clone, Serialize)]
struct Settings {
    automatic: bool,
    allow_battery_boost: bool,
    performance_profile: bool,
    manual_enabled: bool,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            // The shell sends the persisted user preference after connecting.
            // Avoid applying a guessed default before that first handshake.
            automatic: false,
            allow_battery_boost: false,
            performance_profile: true,
            manual_enabled: false,
        }
    }
}

#[derive(Default, Serialize)]
struct RuntimeState {
    detected_game: bool,
    game_processes: usize,
    automatic_latched: bool,
    dismissed_for_game: bool,
    gaming_active: bool,
    gamemode_available: bool,
    gamemode_active: bool,
    registered_processes: usize,
    power_source: String,
    active_power_profile: String,
    performance_profile_available: bool,
}

#[derive(Default, Serialize)]
struct SharedState {
    settings: Settings,
    runtime: RuntimeState,
}

#[derive(Clone)]
struct GamingService {
    state: Arc<Mutex<SharedState>>,
}

#[derive(Debug, Serialize, Deserialize)]
struct ProfileJournal {
    previous_profile: String,
}

fn log(level: &str, event: &str, fields: Value) {
    eprintln!(
        "{}",
        json!({"level": level, "service": "daisy-gamingd", "event": event, "fields": fields})
    );
}

async fn authorize(header: &Header<'_>, connection: &zbus::Connection) -> Result<(), Error> {
    let sender = header
        .sender()
        .ok_or_else(|| Error::AccessDenied("missing caller identity".into()))?;
    let proxy = zbus::fdo::DBusProxy::new(connection).await?;
    let uid = proxy
        .get_connection_unix_user(sender.as_ref().into())
        .await
        .map_err(zbus::Error::from)?;
    // SAFETY: getuid has no arguments or memory preconditions.
    if uid != unsafe { libc::getuid() } {
        return Err(Error::AccessDenied(
            "caller is not this session user".into(),
        ));
    }
    Ok(())
}

#[interface(name = "org.daisyos.Gaming1")]
impl GamingService {
    async fn status(
        &self,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<String, Error> {
        authorize(&header, connection).await?;
        let state = self.state.lock().unwrap();
        serde_json::to_string(&*state)
            .map_err(|_| Error::InvalidArgument("status serialization failed".into()))
    }

    async fn set_manual_enabled(
        &self,
        enabled: bool,
        user_initiated: bool,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<(), Error> {
        authorize(&header, connection).await?;
        let mut state = self.state.lock().unwrap();
        state.settings.manual_enabled = enabled;
        if enabled {
            state.runtime.automatic_latched = false;
            state.runtime.dismissed_for_game = false;
        } else if user_initiated && state.runtime.detected_game {
            state.runtime.automatic_latched = false;
            state.runtime.dismissed_for_game = true;
        }
        state.runtime.gaming_active =
            state.settings.manual_enabled || state.runtime.automatic_latched;
        log("info", "manual_mode_changed", json!({"enabled": enabled}));
        Ok(())
    }

    async fn configure(
        &self,
        automatic: bool,
        allow_battery_boost: bool,
        performance_profile: bool,
        #[zbus(header)] header: Header<'_>,
        #[zbus(connection)] connection: &zbus::Connection,
    ) -> Result<(), Error> {
        authorize(&header, connection).await?;
        let mut state = self.state.lock().unwrap();
        if state.settings.automatic && !automatic && state.runtime.automatic_latched {
            state.runtime.automatic_latched = false;
            state.runtime.dismissed_for_game = state.runtime.detected_game;
        }
        state.settings.automatic = automatic;
        state.settings.allow_battery_boost = allow_battery_boost;
        state.settings.performance_profile = performance_profile;
        if automatic
            && state.runtime.detected_game
            && !state.settings.manual_enabled
            && !state.runtime.dismissed_for_game
        {
            state.runtime.automatic_latched = true;
        }
        state.runtime.gaming_active =
            state.settings.manual_enabled || state.runtime.automatic_latched;
        log(
            "info",
            "settings_changed",
            json!({"automatic": automatic, "allow_battery_boost": allow_battery_boost, "performance_profile": performance_profile}),
        );
        Ok(())
    }
}

fn read(path: &Path) -> Option<String> {
    fs::read_to_string(path).ok().map(|s| s.trim().to_owned())
}

async fn performance_profile_available() -> bool {
    command_output("powerprofilesctl", &["list"])
        .await
        .is_some_and(|output| {
            output.lines().any(|line| {
                line.trim()
                    .trim_start_matches('*')
                    .trim()
                    .trim_end_matches(':')
                    == "performance"
            })
        })
}

fn game_processes() -> HashSet<i32> {
    let mut roots = Vec::new();
    let Ok(entries) = fs::read_dir("/proc") else {
        return HashSet::new();
    };
    for entry in entries.flatten() {
        let name = entry.file_name();
        let Some(pid) = name.to_str().and_then(|value| value.parse::<i32>().ok()) else {
            continue;
        };
        let Some(cmdline) = read(&entry.path().join("cmdline")) else {
            continue;
        };
        let args: Vec<&str> = cmdline.split('\0').filter(|arg| !arg.is_empty()).collect();
        let steam = args.first().is_some_and(|arg| {
            Path::new(arg)
                .file_name()
                .is_some_and(|name| name == "reaper")
        }) && args.contains(&"SteamLaunch")
            && args
                .iter()
                .any(|arg| arg.starts_with("AppId=") && *arg != "AppId=0");
        let lutris = args.iter().take(2).any(|arg| {
            Path::new(arg)
                .file_name()
                .is_some_and(|name| name == "lutris-wrapper")
        });
        if steam || lutris {
            roots.push(pid);
        }
    }
    let mut found = HashSet::new();
    for pid in roots {
        add_process_tree(pid, &mut found);
    }
    found
}

fn add_process_tree(pid: i32, found: &mut HashSet<i32>) {
    if !found.insert(pid) {
        return;
    }
    let path = PathBuf::from(format!("/proc/{pid}/task/{pid}/children"));
    if let Some(children) = read(&path) {
        for child in children
            .split_ascii_whitespace()
            .filter_map(|value| value.parse::<i32>().ok())
        {
            add_process_tree(child, found);
        }
    }
}

fn on_battery() -> bool {
    let Ok(entries) = fs::read_dir("/sys/class/power_supply") else {
        return false;
    };
    let mut battery = false;
    let mut external_power = false;
    for entry in entries.flatten() {
        let path = entry.path();
        match read(&path.join("type")).as_deref() {
            Some("Battery") => battery = true,
            Some("Mains") if read(&path.join("online")).as_deref() == Some("1") => {
                external_power = true
            }
            _ => {}
        }
    }
    battery && !external_power
}

fn journal_path() -> Option<PathBuf> {
    std::env::var_os("XDG_RUNTIME_DIR")
        .map(|dir| PathBuf::from(dir).join("daisyos-gamingd/profile.json"))
}

fn write_journal(previous: &str) -> std::io::Result<()> {
    let path = journal_path().ok_or_else(|| std::io::Error::other("XDG_RUNTIME_DIR is missing"))?;
    let parent = path
        .parent()
        .ok_or_else(|| std::io::Error::other("invalid runtime path"))?;
    fs::create_dir_all(parent)?;
    let temporary = path.with_extension("tmp");
    fs::write(
        &temporary,
        serde_json::to_vec(&ProfileJournal {
            previous_profile: previous.to_owned(),
        })?,
    )?;
    fs::rename(temporary, path)
}

fn remove_journal() {
    if let Some(path) = journal_path() {
        let _ = fs::remove_file(path);
    }
}

async fn command_output(program: &str, args: &[&str]) -> Option<String> {
    let mut command = Command::new(program);
    command
        .args(args)
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .kill_on_drop(true);
    let output = time::timeout(Duration::from_secs(3), command.output())
        .await
        .ok()?
        .ok()?;
    if !output.status.success() {
        return None;
    }
    Some(String::from_utf8_lossy(&output.stdout).trim().to_owned())
}

async fn power_profile() -> Option<String> {
    let profile = command_output("powerprofilesctl", &["get"]).await?;
    matches!(profile.as_str(), "power-saver" | "balanced" | "performance").then_some(profile)
}

async fn restore_profile(reason: &str) {
    let Some(path) = journal_path() else { return };
    let Ok(data) = fs::read(&path) else { return };
    let Ok(journal) = serde_json::from_slice::<ProfileJournal>(&data) else {
        remove_journal();
        return;
    };
    if !matches!(
        journal.previous_profile.as_str(),
        "power-saver" | "balanced"
    ) {
        remove_journal();
        return;
    }
    match power_profile().await.as_deref() {
        Some("performance") => {
            if command_output("powerprofilesctl", &["set", &journal.previous_profile])
                .await
                .is_some()
            {
                log(
                    "info",
                    "profile_restored",
                    json!({"profile": journal.previous_profile, "reason": reason}),
                );
                remove_journal();
            } else {
                log("error", "profile_restore_failed", json!({"reason": reason}));
            }
        }
        Some(_) => {
            log(
                "info",
                "profile_restore_skipped_user_change",
                json!({"reason": reason}),
            );
            remove_journal();
        }
        None => log(
            "error",
            "profile_read_failed_during_restore",
            json!({"reason": reason}),
        ),
    }
}

async fn set_performance_profile(active: bool, available: bool) -> bool {
    if !active {
        restore_profile("gaming_stopped").await;
        return false;
    }
    if !available {
        return false;
    }
    if journal_path().is_some_and(|path| path.exists()) {
        return true;
    }
    let Some(current) = power_profile().await else {
        return false;
    };
    if current == "performance" {
        return true;
    }
    if !matches!(current.as_str(), "balanced" | "power-saver") {
        return false;
    }
    if let Err(error) = write_journal(&current) {
        log(
            "error",
            "profile_journal_write_failed",
            json!({"error": error.to_string()}),
        );
        return false;
    }
    if command_output("powerprofilesctl", &["set", "performance"])
        .await
        .is_some()
    {
        log(
            "info",
            "performance_profile_activated",
            json!({"previous": current}),
        );
        true
    } else {
        remove_journal();
        false
    }
}

struct GameModeApi {
    handle: *mut libc::c_void,
    start: Option<unsafe extern "C" fn(i32) -> i32>,
    end: Option<unsafe extern "C" fn(i32) -> i32>,
}
impl GameModeApi {
    fn load() -> Self {
        let name = b"libgamemode.so.0\0";
        let handle =
            unsafe { libc::dlopen(name.as_ptr().cast(), libc::RTLD_NOW | libc::RTLD_LOCAL) };
        if handle.is_null() {
            return Self {
                handle,
                start: None,
                end: None,
            };
        }
        unsafe fn symbol(
            handle: *mut libc::c_void,
            name: &'static [u8],
        ) -> Option<unsafe extern "C" fn(i32) -> i32> {
            let pointer = libc::dlsym(handle, name.as_ptr().cast());
            if pointer.is_null() {
                None
            } else {
                Some(std::mem::transmute(pointer))
            }
        }
        Self {
            handle,
            start: unsafe { symbol(handle, b"real_gamemode_request_start_for\0") },
            end: unsafe { symbol(handle, b"real_gamemode_request_end_for\0") },
        }
    }
    fn register(&self, pid: i32) {
        if let Some(f) = self.start {
            unsafe {
                f(pid);
            }
        }
    }
    fn unregister(&self, pid: i32) {
        if let Some(f) = self.end {
            unsafe {
                f(pid);
            }
        }
    }
}
impl Drop for GameModeApi {
    fn drop(&mut self) {
        if !self.handle.is_null() {
            unsafe {
                libc::dlclose(self.handle);
            }
        }
    }
}

async fn start_gamemode(holder: &mut Option<Child>, next_retry: &mut tokio::time::Instant) -> bool {
    if holder
        .as_mut()
        .and_then(|child| child.try_wait().ok())
        .flatten()
        .is_some()
    {
        *holder = None;
    }
    if holder.is_some() {
        return true;
    }
    if tokio::time::Instant::now() < *next_retry {
        return false;
    }
    match Command::new("gamemoderun")
        .args(["sleep", "infinity"])
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .kill_on_drop(true)
        .spawn()
    {
        Ok(child) => {
            *holder = Some(child);
            log("info", "gamemode_started", json!({}));
            true
        }
        Err(error) => {
            *next_retry = tokio::time::Instant::now() + Duration::from_secs(30);
            log(
                "warn",
                "gamemode_unavailable",
                json!({"error": error.to_string(), "retry_seconds": 30}),
            );
            false
        }
    }
}

async fn stop_gamemode(holder: &mut Option<Child>) {
    if let Some(mut child) = holder.take() {
        let _ = child.kill().await;
        let _ = child.wait().await;
        log("info", "gamemode_stopped", json!({}));
    }
}

async fn run_loop(
    state: Arc<Mutex<SharedState>>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let api = GameModeApi::load();
    let mut holder: Option<Child> = None;
    let mut registered = HashSet::new();
    let mut next_gamemode_retry = tokio::time::Instant::now();
    let mut profile_available = false;
    let mut next_profile_capability_check = tokio::time::Instant::now();
    let mut current_profile = "unavailable".to_owned();
    let mut next_profile_status_check = tokio::time::Instant::now();
    let mut profile_transaction_active = false;
    let mut next_profile_attempt = tokio::time::Instant::now();
    let mut tick = time::interval(PROFILE_POLL);
    let mut terminate = tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())?;
    let ctrl_c = tokio::signal::ctrl_c();
    tokio::pin!(ctrl_c);
    loop {
        tokio::select! { _ = terminate.recv() => break, _ = &mut ctrl_c => break, _ = tick.tick() => {} }
        let processes = game_processes();
        let battery = on_battery();
        let (active, use_profile) = {
            let mut shared = state.lock().unwrap();
            let automatic = shared.settings.automatic;
            let manual_enabled = shared.settings.manual_enabled;
            let allow_battery_boost = shared.settings.allow_battery_boost;
            let performance_profile = shared.settings.performance_profile;
            let runtime = &mut shared.runtime;
            let was_detected = runtime.detected_game;
            runtime.detected_game = !processes.is_empty();
            runtime.game_processes = processes.len();
            runtime.power_source = if battery { "battery" } else { "ac_or_desktop" }.into();
            if !runtime.detected_game {
                runtime.automatic_latched = false;
                runtime.dismissed_for_game = false;
            } else if !was_detected && automatic && !manual_enabled {
                runtime.automatic_latched = true;
            }
            let active = manual_enabled || runtime.automatic_latched;
            runtime.gaming_active = active;
            let performance_allowed = !battery || allow_battery_boost;
            (
                active && performance_allowed,
                active && performance_allowed && performance_profile,
            )
        };
        let now = tokio::time::Instant::now();
        if now >= next_profile_capability_check {
            profile_available = performance_profile_available().await;
            next_profile_capability_check = now + Duration::from_secs(60);
        }
        let mut profile_changed = false;
        if use_profile && !profile_transaction_active && now >= next_profile_attempt {
            profile_transaction_active = set_performance_profile(true, profile_available).await;
            next_profile_attempt = now + Duration::from_secs(30);
            profile_changed = profile_transaction_active;
        } else if !use_profile && profile_transaction_active {
            set_performance_profile(false, profile_available).await;
            profile_transaction_active = false;
            next_profile_attempt = now;
            profile_changed = true;
        }
        let gamemode_active = if active {
            start_gamemode(&mut holder, &mut next_gamemode_retry).await
        } else {
            next_gamemode_retry = tokio::time::Instant::now();
            stop_gamemode(&mut holder).await;
            false
        };
        if gamemode_active {
            for pid in registered.difference(&processes) {
                api.unregister(*pid);
            }
            for pid in processes.difference(&registered) {
                api.register(*pid);
            }
            registered = processes;
        } else {
            for pid in registered.drain() {
                api.unregister(pid);
            }
        }
        if profile_changed || now >= next_profile_status_check {
            current_profile = power_profile()
                .await
                .unwrap_or_else(|| "unavailable".into());
            next_profile_status_check = now + Duration::from_secs(15);
        }
        let mut shared = state.lock().unwrap();
        shared.runtime.gamemode_available = api.start.is_some() && api.end.is_some();
        shared.runtime.gamemode_active = gamemode_active;
        shared.runtime.registered_processes = registered.len();
        shared.runtime.active_power_profile = current_profile.clone();
        shared.runtime.performance_profile_available = profile_available;
    }
    for pid in registered {
        api.unregister(pid);
    }
    stop_gamemode(&mut holder).await;
    restore_profile("daemon_shutdown").await;
    Ok(())
}

#[tokio::main(flavor = "current_thread")]
async fn main() -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    restore_profile("daemon_startup_recovery").await;
    let state = Arc::new(Mutex::new(SharedState::default()));
    let service = GamingService {
        state: state.clone(),
    };
    let connection = zbus::connection::Builder::session()?
        .serve_at(PATH, service)?
        .name(BUS)?
        .build()
        .await?;
    log(
        "info",
        "daemon_ready",
        json!({"uid": unsafe { libc::getuid() }}),
    );
    let mut messages = zbus::MessageStream::from(&connection);
    use std::future::poll_fn;
    use zbus::export::futures_core::Stream;
    let bus_closed = async {
        while poll_fn(|cx| std::pin::Pin::new(&mut messages).poll_next(cx))
            .await
            .is_some()
        {}
    };
    tokio::select! {
        result = run_loop(state) => result?,
        _ = bus_closed => return Err("session D-Bus connection closed".into())
    }
    drop(connection);
    Ok(())
}
