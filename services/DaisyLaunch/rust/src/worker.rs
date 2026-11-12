//! A single KDE worker with a short idle lease. Taking it out of the pool makes
//! cancellation drop/kill it rather than leaving an unread response for reuse.
use super::*;
use tokio::{
    io::{AsyncBufReadExt, AsyncReadExt, BufReader},
    process::{Child, ChildStdin, ChildStdout},
};
const IDLE: Duration = Duration::from_secs(2);
pub struct Worker {
    child: Child,
    input: ChildStdin,
    output: BufReader<ChildStdout>,
    last_used: Instant,
}
impl Worker {
    async fn line(&mut self) -> std::io::Result<Value> {
        let mut line = String::new();
        let count = (&mut self.output).take(4097).read_line(&mut line).await?;
        if count == 0 || count > 4096 || !line.ends_with('\n') {
            return Err(std::io::Error::other("Invalid helper response"));
        }
        serde_json::from_str(&line).map_err(std::io::Error::other)
    }
}
#[derive(Default)]
pub struct Pool {
    slot: tokio::sync::Mutex<Option<Worker>>,
    changed: tokio::sync::Notify,
}
impl Pool {
    pub async fn stop(&self) {
        if let Some(mut worker) = self.slot.lock().await.take() {
            let _ = worker.child.kill().await;
        }
    }
    pub async fn exchange(
        &self,
        backend: &std::path::Path,
        payload: &str,
    ) -> std::io::Result<Value> {
        let mut cached = self.slot.lock().await.take();
        if let Some(worker) = cached.as_mut() {
            if worker.last_used.elapsed() >= IDLE || worker.child.try_wait()?.is_some() {
                if let Some(mut expired) = cached.take() {
                    let _ = expired.child.kill().await;
                }
            }
        }
        let mut worker = match cached {
            Some(worker) => worker,
            expired => {
                if let Some(mut worker) = expired {
                    let _ = worker.child.kill().await;
                }
                let mut child = Command::new(backend)
                    .arg("--broker-worker")
                    .stdin(std::process::Stdio::piped())
                    .stdout(std::process::Stdio::piped())
                    .stderr(std::process::Stdio::null())
                    .kill_on_drop(true)
                    .spawn()?;
                let input = child.stdin.take().unwrap();
                let output = BufReader::new(child.stdout.take().unwrap());
                let mut worker = Worker {
                    child,
                    input,
                    output,
                    last_used: Instant::now(),
                };
                if worker.line().await?.get("ready").and_then(Value::as_bool) != Some(true) {
                    return Err(std::io::Error::other("Invalid helper handshake"));
                }
                worker
            }
        };
        worker.input.write_all(payload.as_bytes()).await?;
        worker.input.write_all(b"\n").await?;
        let value = worker.line().await?;
        worker.last_used = Instant::now();
        *self.slot.lock().await = Some(worker);
        self.changed.notify_one();
        Ok(value)
    }
}
pub async fn reap(pool: Arc<Pool>) {
    loop {
        // Stay asleep while the pool is empty. A returned worker schedules its
        // own lease deadline instead of waking the resident broker at 10 Hz.
        let changed = pool.changed.notified();
        let remaining = {
            let slot = pool.slot.lock().await;
            slot.as_ref()
                .map(|worker| IDLE.saturating_sub(worker.last_used.elapsed()))
        };
        match remaining {
            None => changed.await,
            Some(remaining) if !remaining.is_zero() => {
                tokio::select! {
                    _ = changed => {},
                    _ = tokio::time::sleep(remaining) => {},
                }
            }
            Some(_) => {
                let expired = {
                    let mut slot = pool.slot.lock().await;
                    if slot
                        .as_ref()
                        .is_some_and(|worker| worker.last_used.elapsed() >= IDLE)
                    {
                        slot.take()
                    } else {
                        None
                    }
                };
                if let Some(mut worker) = expired {
                    let _ = worker.child.kill().await;
                }
            }
        }
    }
}
