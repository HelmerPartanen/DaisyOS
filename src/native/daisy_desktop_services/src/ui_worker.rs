//! On-demand Avalonia UI helper lease manager.
//! Starts `DaisyOS.Shell --desktop-services-ui-worker --bus-name org.daisyos.DesktopServices.UI`
//! only when an interactive portal dialog or sharing indicator is needed, and reaps it
//! once all interactive requests and sessions have closed.

use std::{
    path::PathBuf,
    process::Stdio,
    sync::{
        atomic::{AtomicUsize, Ordering},
        Arc,
    },
    time::{Duration, Instant},
};
use tokio::{
    io::{AsyncBufReadExt, BufReader},
    process::{Child, Command},
    sync::Mutex,
};

pub const UI_BUS_NAME: &str = "org.daisyos.DesktopServices.UI";
const IDLE_LEASE: Duration = Duration::from_secs(2);

struct WorkerProcess {
    child: Child,
    last_used: Instant,
}

#[derive(Clone)]
pub struct UiWorker {
    ui_binary: PathBuf,
    process: Arc<Mutex<Option<WorkerProcess>>>,
    active_count: Arc<AtomicUsize>,
}

pub struct UiLease {
    worker: UiWorker,
}

impl Drop for UiLease {
    fn drop(&mut self) {
        if self.worker.active_count.fetch_sub(1, Ordering::SeqCst) == 1 {
            let process = self.worker.process.clone();
            tokio::spawn(async move {
                if let Some(ref mut w) = *process.lock().await {
                    w.last_used = Instant::now();
                }
            });
        }
    }
}

impl UiWorker {
    pub fn new(ui_binary: PathBuf) -> Self {
        Self {
            ui_binary,
            process: Arc::new(Mutex::new(None)),
            active_count: Arc::new(AtomicUsize::new(0)),
        }
    }

    pub async fn acquire(&self, connection: &zbus::Connection) -> Result<UiLease, zbus::Error> {
        self.active_count.fetch_add(1, Ordering::SeqCst);
        let lease = UiLease {
            worker: self.clone(),
        };
        let mut guard = self.process.lock().await;
        if let Some(ref mut existing) = *guard {
            if existing.child.try_wait().ok().flatten().is_none() {
                existing.last_used = Instant::now();
                return Ok(lease);
            }
            *guard = None;
        }

        let mut child = Command::new(&self.ui_binary)
            .arg("--desktop-services-ui-worker")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .kill_on_drop(true)
            .spawn()
            .map_err(|e| zbus::Error::Failure(format!("Failed to start UI worker: {e}")))?;

        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| zbus::Error::Failure("Missing worker stdout".into()))?;
        let mut reader = BufReader::new(stdout).lines();

        let ready = tokio::time::timeout(Duration::from_secs(15), reader.next_line())
            .await
            .map_err(|_| zbus::Error::Failure("UI worker startup timed out".into()))?
            .map_err(|e| zbus::Error::Failure(format!("UI worker read error: {e}")))?;

        if ready.as_deref() != Some("ready") {
            let _ = child.kill().await;
            return Err(zbus::Error::Failure("UI worker did not become ready".into()));
        }

        // Verify name is on bus
        let dbus = zbus::fdo::DBusProxy::new(connection).await?;
        for _ in 0..50 {
            if dbus.name_has_owner(UI_BUS_NAME.try_into().unwrap()).await.unwrap_or(false) {
                break;
            }
            tokio::time::sleep(Duration::from_millis(20)).await;
        }

        *guard = Some(WorkerProcess {
            child,
            last_used: Instant::now(),
        });
        Ok(lease)
    }

    pub async fn forward_if_running(&self, connection: &zbus::Connection, msg: &zbus::Message) {
        let running = {
            let mut guard = self.process.lock().await;
            if let Some(ref mut w) = *guard {
                if w.child.try_wait().ok().flatten().is_none() {
                    true
                } else {
                    *guard = None;
                    false
                }
            } else {
                false
            }
        };
        if !running {
            return;
        }
        let header = msg.header();
        let Some(path) = header.path().map(|p| p.to_owned()) else {
            return;
        };
        let Some(iface) = header.interface().map(|i| i.to_owned()) else {
            return;
        };
        let Some(member) = header.member().map(|m| m.to_owned()) else {
            return;
        };
        let _ = connection
            .call_method(Some(UI_BUS_NAME), path, Some(iface), member, &())
            .await;
    }

    pub async fn stop(&self) {
        if let Some(mut w) = self.process.lock().await.take() {
            let _ = w.child.kill().await;
        }
    }

    pub fn spawn_reaper(&self) {
        let worker = self.clone();
        tokio::spawn(async move {
            let mut interval = tokio::time::interval(Duration::from_millis(250));
            loop {
                interval.tick().await;
                if worker.active_count.load(Ordering::SeqCst) > 0 {
                    continue;
                }
                let expired = {
                    let mut guard = worker.process.lock().await;
                    if worker.active_count.load(Ordering::SeqCst) == 0
                        && guard
                            .as_ref()
                            .is_some_and(|w| w.last_used.elapsed() >= IDLE_LEASE)
                    {
                        guard.take()
                    } else {
                        None
                    }
                };
                if let Some(mut w) = expired {
                    let _ = w.child.kill().await;
                }
            }
        });
    }
}
