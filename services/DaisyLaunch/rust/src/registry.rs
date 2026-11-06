use super::*;
use std::{
    collections::HashSet,
    ffi::CString,
    os::fd::{AsRawFd, FromRawFd, OwnedFd},
    path::{Path, PathBuf},
};
use tokio::io::unix::AsyncFd;

pub async fn catalog(backend: &Path) -> Result<String, Box<dyn std::error::Error + Send + Sync>> {
    let mut command = Command::new(backend);
    command
        .arg("--catalog")
        .stdin(std::process::Stdio::null())
        .kill_on_drop(true);
    let output = tokio::time::timeout(Duration::from_secs(15), command.output()).await??;
    if !output.status.success() || output.stdout.len() > 16 * 1024 * 1024 {
        return Err("Catalog helper failed".into());
    }
    let text = String::from_utf8(output.stdout)?;
    // Validate without materializing a second copy of every catalog string.
    // The resident broker only serves the serialized snapshot.
    if !text.trim_start().starts_with('[') {
        return Err("Invalid catalog".into());
    }
    use serde::Deserialize;
    let mut deserializer = serde_json::Deserializer::from_str(&text);
    serde::de::IgnoredAny::deserialize(&mut deserializer)?;
    deserializer.end()?;
    Ok(text)
}
fn roots() -> Vec<PathBuf> {
    let home = std::env::var_os("HOME")
        .map(PathBuf::from)
        .unwrap_or_default();
    let data = std::env::var_os("XDG_DATA_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .unwrap_or_else(|| home.join(".local/share"));
    let mut paths = vec![
        data.join("applications"),
        home.join(".local/share/flatpak/exports/share/applications"),
        PathBuf::from("/var/lib/flatpak/exports/share/applications"),
    ];
    for dir in std::env::var("XDG_DATA_DIRS")
        .unwrap_or_else(|_| "/usr/local/share:/usr/share".into())
        .split(':')
        .filter(|s| !s.is_empty())
    {
        if Path::new(dir).is_absolute() {
            paths.push(Path::new(dir).join("applications"));
        }
    }
    paths
}
fn add_tree(fd: i32, path: &Path, watched: &mut HashSet<PathBuf>) {
    if watched.len() >= 4096 || !path.is_dir() || watched.contains(path) {
        return;
    }
    use std::os::unix::ffi::OsStrExt;
    if let Ok(name) = CString::new(path.as_os_str().as_bytes()) {
        // SAFETY: fd is owned by watch, name is a valid nul-terminated path.
        let result = unsafe {
            libc::inotify_add_watch(
                fd,
                name.as_ptr(),
                libc::IN_CREATE
                    | libc::IN_DELETE
                    | libc::IN_CLOSE_WRITE
                    | libc::IN_MOVED_FROM
                    | libc::IN_MOVED_TO
                    | libc::IN_DELETE_SELF
                    | libc::IN_MOVE_SELF,
            )
        };
        if result >= 0 {
            watched.insert(path.to_path_buf());
        }
    }
    if let Ok(entries) = std::fs::read_dir(path) {
        for entry in entries.flatten() {
            if entry.file_type().is_ok_and(|kind| kind.is_dir()) {
                add_tree(fd, &entry.path(), watched);
            }
        }
    }
}
fn install(fd: i32, roots: &[PathBuf], watched: &mut HashSet<PathBuf>) {
    for root in roots {
        let mut parent = root.as_path();
        while !parent.is_dir() {
            let Some(next) = parent.parent() else { break };
            parent = next;
        }
        // Only recurse into application directories; watch nearest existing
        // ancestor nonrecursively when a root does not yet exist.
        if root.is_dir() {
            add_tree(fd, root, watched);
        } else if !watched.contains(parent) {
            use std::os::unix::ffi::OsStrExt;
            if let Ok(name) = CString::new(parent.as_os_str().as_bytes()) {
                if unsafe {
                    libc::inotify_add_watch(fd, name.as_ptr(), libc::IN_CREATE | libc::IN_MOVED_TO)
                } >= 0
                {
                    watched.insert(parent.to_path_buf());
                }
            }
        }
    }
}
pub async fn watch(launcher: Launcher, connection: zbus::Connection) {
    // SAFETY: returns a fresh owned descriptor, or -1 on failure.
    let fd = unsafe { libc::inotify_init1(libc::IN_NONBLOCK | libc::IN_CLOEXEC) };
    if fd < 0 {
        eprintln!("[daisy-launch] catalog watch unavailable");
        return;
    }
    let Ok(fd) = (unsafe { AsyncFd::register(OwnedFd::from_raw_fd(fd)) }) else {
        return;
    };
    let roots = roots();
    let mut watched = HashSet::new();
    install(fd.as_raw_fd(), &roots, &mut watched);
    loop {
        let Ok(mut ready) = fd.readable().await else {
            return;
        };
        let result = ready.try_io(|inner| {
            let mut buffer = [0u8; 16384];
            let n =
                unsafe { libc::read(inner.as_raw_fd(), buffer.as_mut_ptr().cast(), buffer.len()) };
            if n < 0 {
                Err(std::io::Error::last_os_error())
            } else {
                Ok(n)
            }
        });
        match result {
            Err(_) => continue,
            Ok(Err(_)) => return,
            Ok(Ok(_)) => {}
        }
        tokio::time::sleep(Duration::from_millis(300)).await;
        watched.clear();
        install(fd.as_raw_fd(), &roots, &mut watched);
        let Ok(_permit) = launcher.helpers.acquire().await else {
            return;
        };
        launcher.workers.stop().await;
        let mut builder = Command::new("kbuildsycoca6");
        builder
            .arg("--noincremental")
            .stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null())
            .kill_on_drop(true);
        if matches!(tokio::time::timeout(Duration::from_secs(15), builder.status()).await, Ok(Ok(status)) if status.success())
        {
            if let Ok(catalog) = catalog(&launcher.backend).await {
                *launcher.catalog.lock().unwrap() = catalog;
                let _ = connection
                    .emit_signal(None::<&str>, PATH, IFACE, "RegistryChanged", &())
                    .await;
            }
        }
    }
}
