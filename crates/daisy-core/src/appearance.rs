//! Existing numeric enums and camelCase appearance.json are retained.
use serde_json::Value;
use std::{io, path::PathBuf};
#[derive(Clone, Debug)]
pub struct Appearance {
    pub document: Value,
}
impl Default for Appearance {
    fn default() -> Self {
        Self {
            document: serde_json::json!({"themeMode":1,"accentColor":8,"reduceMotion":false,"highContrast":false,"textScalePercent":100,"systemFontFamily":"Atkinson Hyperlegible","languageTag":"en"}),
        }
    }
}
impl Appearance {
    pub fn path() -> io::Result<PathBuf> {
        let base = std::env::var_os("XDG_CONFIG_HOME")
            .map(PathBuf::from)
            .filter(|p| p.is_absolute())
            .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")))
            .ok_or_else(|| io::Error::other("No configuration home"))?;
        Ok(base.join("DaisyOS/appearance.json"))
    }
    pub fn load(path: &std::path::Path) -> io::Result<Self> {
        match std::fs::read(path) {
            Ok(bytes) => {
                let document: Value = serde_json::from_slice(&bytes).map_err(io::Error::other)?;
                if !document.is_object() {
                    return Err(io::Error::other("Appearance settings must be an object"));
                }
                Ok(Self { document })
            }
            Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(Self::default()),
            Err(e) => Err(e),
        }
    }
    pub fn get(&self, key: &str) -> Option<&Value> {
        self.document
            .as_object()?
            .iter()
            .find(|(k, _)| k.eq_ignore_ascii_case(key))
            .map(|(_, value)| value)
    }
    pub fn reduce_motion(&self) -> bool {
        self.get("reduceMotion")
            .and_then(Value::as_bool)
            .unwrap_or(false)
    }
    pub fn font(&self) -> &str {
        self.get("systemFontFamily")
            .and_then(Value::as_str)
            .filter(|s| !s.is_empty())
            .unwrap_or("Atkinson Hyperlegible")
    }
    pub fn scale(&self) -> f32 {
        self.get("textScalePercent")
            .and_then(Value::as_f64)
            .unwrap_or(100.)
            .clamp(75., 200.) as f32
            / 100.
    }
    pub fn dark(&self) -> bool {
        match self.get("themeMode").and_then(Value::as_i64).unwrap_or(1) {
            0 => false,
            2 => automatic_dark(),
            _ => true,
        }
    }
    pub fn accent_index(&self) -> usize {
        self.get("accentColor")
            .and_then(Value::as_u64)
            .unwrap_or(8)
            .min(8) as usize
    }
}

/// Local timezone coordinates and NOAA schedule mirror AutomaticThemeScheduler.cs.
pub fn automatic_dark() -> bool {
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
        let Ok(text) = std::fs::read_to_string(path) else {
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

pub fn message(language: &str, source: &str) -> String {
    let data: Value = serde_json::from_str(include_str!("calculator-messages.json"))
        .expect("generated RESX messages");
    data["messages"][if language.to_ascii_lowercase().starts_with("fi") {
        "fi-FI"
    } else {
        "en-US"
    }][source]
        .as_str()
        .unwrap_or(source)
        .to_owned()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn retains_unknown_fields_and_numeric_enums() {
        let a = Appearance {
            document: serde_json::json!({"ThemeMode":0,"AccentColor":5,"CustomPlugin":{"enabled":true}}),
        };
        assert!(!a.dark());
        assert_eq!(a.accent_index(), 5);
        assert!(a.document.get("CustomPlugin").is_some());
    }
    #[test]
    fn iso_timezone_coordinates() {
        assert!((coordinate("+6010", 2).unwrap() - 60.1666666667).abs() < 1e-8);
        assert!((coordinate("-0740023", 3).unwrap() + 74.006388889).abs() < 1e-8);
    }
    #[test]
    fn unknown_accent_is_default() {
        let a = Appearance {
            document: serde_json::json!({"accentColor":999}),
        };
        assert_eq!(a.accent_index(), 8);
    }
}
