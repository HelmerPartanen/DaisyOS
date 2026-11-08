//! Portal Settings implementation and appearance.json watcher.
//! Mirrors `PortalSettings.cs` and `AutomaticThemeScheduler.cs` without loading Avalonia.

use serde_json::Value;
use std::{
    collections::HashMap,
    ffi::CString,
    os::fd::{AsRawFd, FromRawFd, OwnedFd},
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
    time::Duration,
};
use tokio::io::unix::AsyncFd;
use zbus::zvariant::OwnedValue;

pub const INTERFACE: &str = "org.freedesktop.impl.portal.Settings";
const DEFAULT_FONT_FAMILY: &str = "Inter";

#[derive(Clone, Debug, PartialEq)]
pub struct AppearanceSnapshot {
    pub color_scheme: u32,
    pub contrast: u32,
    pub reduced_motion: u32,
    pub text_scaling_factor: f64,
    pub font_name: String,
}

impl AppearanceSnapshot {
    pub fn load() -> Self {
        let doc = appearance_path()
            .and_then(|path| std::fs::read(&path).ok())
            .and_then(|bytes| serde_json::from_slice::<Value>(&bytes).ok())
            .filter(Value::is_object)
            .unwrap_or_else(|| Value::Object(serde_json::Map::new()));
        Self::from_document(&doc)
    }

    pub fn from_document(doc: &Value) -> Self {
        let get = |key: &str| -> Option<&Value> {
            doc.as_object()?
                .iter()
                .find(|(k, _)| k.eq_ignore_ascii_case(key))
                .map(|(_, v)| v)
        };
        let theme_mode = get("themeMode").and_then(Value::as_i64).unwrap_or(1);
        let dark = match theme_mode {
            0 => false,
            2 => automatic_dark(),
            _ => true,
        };
        let high_contrast = get("highContrast").and_then(Value::as_bool).unwrap_or(false);
        let reduce_motion = get("reduceMotion").and_then(Value::as_bool).unwrap_or(false);
        let scale_percent = get("textScalePercent")
            .and_then(Value::as_i64)
            .unwrap_or(100)
            .clamp(90, 150);
        let family = get("systemFontFamily")
            .and_then(Value::as_str)
            .map(str::trim)
            .filter(|s| !s.is_empty())
            .unwrap_or(DEFAULT_FONT_FAMILY);

        Self {
            color_scheme: if dark { 1 } else { 2 },
            contrast: if high_contrast { 1 } else { 0 },
            reduced_motion: if reduce_motion { 1 } else { 0 },
            text_scaling_factor: (scale_percent as f64) / 100.0,
            font_name: format!("{family} 10"),
        }
    }

    pub fn values(&self) -> HashMap<String, HashMap<String, OwnedValue>> {
        let mut appearance = HashMap::new();
        appearance.insert("color-scheme".into(), OwnedValue::from(self.color_scheme));
        appearance.insert("contrast".into(), OwnedValue::from(self.contrast));
        appearance.insert("reduced-motion".into(), OwnedValue::from(self.reduced_motion));

        let mut gtk = HashMap::new();
        gtk.insert(
            "text-scaling-factor".into(),
            OwnedValue::from(self.text_scaling_factor),
        );
        gtk.insert(
            "font-name".into(),
            OwnedValue::try_from(zbus::zvariant::Value::from(self.font_name.as_str())).unwrap(),
        );

        let mut map = HashMap::new();
        map.insert("org.freedesktop.appearance".into(), appearance);
        map.insert("org.gnome.desktop.interface".into(), gtk);
        map
    }
}

pub fn includes_namespace(filters: &[String], ns: &str) -> bool {
    filters.is_empty()
        || filters.iter().any(|filter| {
            if let Some(prefix) = filter.strip_suffix('*') {
                ns.starts_with(prefix)
            } else {
                filter == ns
            }
        })
}

fn appearance_path() -> Option<PathBuf> {
    let base = std::env::var_os("XDG_CONFIG_HOME")
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")))?;
    Some(base.join("DaisyOS/appearance.json"))
}

pub async fn watch(connection: zbus::Connection, last: Arc<Mutex<AppearanceSnapshot>>) {
    let mut interval = tokio::time::interval(Duration::from_secs(60));
    let fd = unsafe { libc::inotify_init1(libc::IN_NONBLOCK | libc::IN_CLOEXEC) };
    let async_fd = if fd >= 0 {
        unsafe { AsyncFd::register(OwnedFd::from_raw_fd(fd)).ok() }
    } else {
        None
    };

    if let Some(ref afd) = async_fd {
        install_watch(afd.as_raw_fd());
    }

    loop {
        if let Some(ref afd) = async_fd {
            tokio::select! {
                _ = interval.tick() => {},
                ready = afd.readable() => {
                    if let Ok(mut guard) = ready {
                        let _ = guard.try_io(|inner| {
                            let mut buf = [0u8; 4096];
                            let n = unsafe { libc::read(inner.as_raw_fd(), buf.as_mut_ptr().cast(), buf.len()) };
                            if n < 0 { Err(std::io::Error::last_os_error()) } else { Ok(n) }
                        });
                        tokio::time::sleep(Duration::from_millis(50)).await;
                        install_watch(afd.as_raw_fd());
                    }
                }
            }
        } else {
            interval.tick().await;
        }
        emit_changes(&connection, &last).await;
    }
}

fn install_watch(fd: i32) {
    let Some(path) = appearance_path() else { return };
    let mut dir = path.parent();
    while let Some(d) = dir {
        if d.is_dir() {
            use std::os::unix::ffi::OsStrExt;
            if let Ok(cstr) = CString::new(d.as_os_str().as_bytes()) {
                unsafe {
                    libc::inotify_add_watch(
                        fd,
                        cstr.as_ptr(),
                        libc::IN_CREATE
                            | libc::IN_MOVED_TO
                            | libc::IN_CLOSE_WRITE
                            | libc::IN_MODIFY
                            | libc::IN_DELETE,
                    );
                }
            }
            break;
        }
        dir = d.parent();
    }
}

async fn emit_changes(connection: &zbus::Connection, last: &Arc<Mutex<AppearanceSnapshot>>) {
    let current = AppearanceSnapshot::load();
    let changes = {
        let mut guard = last.lock().unwrap();
        if *guard == current {
            return;
        }
        let prev = guard.clone();
        *guard = current.clone();
        let mut diff: Vec<(&'static str, &'static str, OwnedValue)> = Vec::new();
        if prev.color_scheme != current.color_scheme {
            diff.push((
                "org.freedesktop.appearance",
                "color-scheme",
                OwnedValue::from(current.color_scheme),
            ));
        }
        if prev.contrast != current.contrast {
            diff.push((
                "org.freedesktop.appearance",
                "contrast",
                OwnedValue::from(current.contrast),
            ));
        }
        if prev.reduced_motion != current.reduced_motion {
            diff.push((
                "org.freedesktop.appearance",
                "reduced-motion",
                OwnedValue::from(current.reduced_motion),
            ));
        }
        if (prev.text_scaling_factor - current.text_scaling_factor).abs() > f64::EPSILON {
            diff.push((
                "org.gnome.desktop.interface",
                "text-scaling-factor",
                OwnedValue::from(current.text_scaling_factor),
            ));
        }
        if prev.font_name != current.font_name {
            diff.push((
                "org.gnome.desktop.interface",
                "font-name",
                OwnedValue::try_from(zbus::zvariant::Value::from(current.font_name.as_str()))
                    .unwrap(),
            ));
        }
        diff
    };

    for (ns, key, val) in changes {
        let _ = connection
            .emit_signal(
                None::<&str>,
                "/org/freedesktop/portal/desktop",
                INTERFACE,
                "SettingChanged",
                &(ns, key, val),
            )
            .await;
    }
}

fn automatic_dark() -> bool {
    unsafe {
        let now = libc::time(std::ptr::null_mut());
        let mut local: libc::tm = std::mem::zeroed();
        if libc::localtime_r(&now, &mut local).is_null() {
            return true;
        }
        let zone = std::env::var("TZ")
            .ok()
            .filter(|z| !z.starts_with(':'))
            .or_else(|| {
                std::fs::read_link("/etc/localtime").ok().and_then(|p| {
                    p.to_str()
                        .and_then(|s| s.split_once("zoneinfo/").map(|(_, z)| z.to_owned()))
                })
            });
        let (lat, lon) = zone
            .as_deref()
            .and_then(zone_coordinates)
            .unwrap_or((0., 0.));
        let offset = local.tm_gmtoff as f64 / 3600.;
        let hour = local.tm_hour as f64 + local.tm_min as f64 / 60. + local.tm_sec as f64 / 3600.;
        let rise = solar_utc(local.tm_yday as f64 + 1., lat, lon, true) + offset;
        let set = solar_utc(local.tm_yday as f64 + 1., lat, lon, false) + offset;
        !(hour >= rise && hour < set)
    }
}

fn zone_coordinates(zone: &str) -> Option<(f64, f64)> {
    for path in [
        "/usr/share/zoneinfo/zone1970.tab",
        "/usr/share/zoneinfo/zone.tab",
    ] {
        let Ok(text) = std::fs::read_to_string(Path::new(path)) else {
            continue;
        };
        for line in text.lines().filter(|l| !l.starts_with('#')) {
            let parts: Vec<_> = line.split('\t').collect();
            if parts.len() < 3 || parts[2] != zone {
                continue;
            }
            let coords = parts[1];
            let split = coords[1..].find(['+', '-'])? + 1;
            return Some((
                coordinate(&coords[..split], 2)?,
                coordinate(&coords[split..], 3)?,
            ));
        }
    }
    None
}

fn coordinate(s: &str, n: usize) -> Option<f64> {
    let digits = &s[1..];
    let sign = if s.starts_with('-') { -1. } else { 1. };
    Some(
        sign * (digits.get(..n)?.parse::<f64>().ok()?
            + digits.get(n..n + 2)?.parse::<f64>().ok()? / 60.
            + digits
                .get(n + 2..)
                .filter(|s| !s.is_empty())
                .unwrap_or("0")
                .parse::<f64>()
                .ok()?
                / 3600.),
    )
}

fn solar_utc(day: f64, lat: f64, lon: f64, rise: bool) -> f64 {
    let lon_hour = lon / 15.;
    let t = day + ((if rise { 6. } else { 18. }) - lon_hour) / 24.;
    let m = 0.9856 * t - 3.289;
    let l = (m + 1.916 * m.to_radians().sin() + 0.020 * (2. * m).to_radians().sin() + 282.634)
        .rem_euclid(360.);
    let mut ra = (0.91764 * l.to_radians().tan())
        .atan()
        .to_degrees()
        .rem_euclid(360.);
    ra += ((l / 90.).floor() - (ra / 90.).floor()) * 90.;
    ra /= 15.;
    let sin_dec = 0.39782 * l.to_radians().sin();
    let cos_dec = sin_dec.asin().cos();
    let cos_h = (90.833f64.to_radians().cos() - sin_dec * lat.to_radians().sin())
        / (cos_dec * lat.to_radians().cos());
    if !(-1. ..=1.).contains(&cos_h) {
        return (if rise { 7. } else { 19. }) - lon_hour;
    }
    let h = (if rise {
        360. - cos_h.acos().to_degrees()
    } else {
        cos_h.acos().to_degrees()
    }) / 15.;
    (h + ra - 0.06571 * t - 6.622 - lon_hour).rem_euclid(24.)
}
