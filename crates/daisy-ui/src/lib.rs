//! Runtime counterpart to generated Slint tokens. Palette and contrast behavior
//! follow App.axaml.cs ApplyAccentPreference; configuration remains read-only.
use daisy_core::appearance::Appearance;
use slint::Color;
#[derive(Clone)]
pub struct ThemeValues {
    pub language: String,
    pub dark: bool,
    pub reduced: bool,
    pub high_contrast: bool,
    pub font: String,
    pub scale: f32,
    pub accent: Color,
    pub hover: Color,
    pub pressed: Color,
    pub content: Color,
}
impl ThemeValues {
    pub fn from_appearance(a: &Appearance) -> Self {
        let dark = a.dark();
        let index = a.accent_index();
        let palettes = include_str!("../themes/source-tokens.json");
        let values: serde_json::Value =
            serde_json::from_str(palettes).expect("generated theme manifest");
        let accent = parse_color(
            values["accents"][if dark { "Dark" } else { "Light" }][index]
                .as_str()
                .expect("generated accent"),
        );
        let black = Color::from_rgb_u8(17, 17, 19);
        let white = Color::from_rgb_u8(255, 255, 255);
        let content = if contrast(accent, black) >= contrast(accent, white) {
            black
        } else {
            white
        };
        let blue = index == 0 || index == 8;
        let target = if blue {
            if dark {
                white
            } else {
                Color::from_rgb_u8(0, 0, 0)
            }
        } else {
            content
        };
        Self {
            language: a
                .get("languageTag")
                .and_then(serde_json::Value::as_str)
                .unwrap_or("en-US")
                .into(),
            dark,
            reduced: a.reduce_motion(),
            high_contrast: a
                .get("highContrast")
                .and_then(serde_json::Value::as_bool)
                .unwrap_or(false),
            font: a.font().into(),
            scale: a.scale(),
            accent,
            content,
            hover: blend(accent, target, if blue { 0.08 } else { 0.12 }),
            pressed: blend(accent, target, if blue { 0.14 } else { 0.22 }),
        }
    }
}
fn parse_color(s: &str) -> Color {
    let v = u32::from_str_radix(s.trim_start_matches('#'), 16).unwrap();
    Color::from_argb_u8(v as u8, (v >> 24) as u8, (v >> 16) as u8, (v >> 8) as u8)
}
fn blend(a: Color, b: Color, t: f64) -> Color {
    let c = |a: u8, b: u8| (a as f64 + (b as f64 - a as f64) * t).round_ties_even() as u8;
    Color::from_rgb_u8(
        c(a.red(), b.red()),
        c(a.green(), b.green()),
        c(a.blue(), b.blue()),
    )
}
fn luminance(c: Color) -> f64 {
    let f = |v: u8| {
        let v = v as f64 / 255.;
        if v <= 0.04045 {
            v / 12.92
        } else {
            ((v + 0.055) / 1.055).powf(2.4)
        }
    };
    0.2126 * f(c.red()) + 0.7152 * f(c.green()) + 0.0722 * f(c.blue())
}
fn contrast(a: Color, b: Color) -> f64 {
    let a = luminance(a);
    let b = luminance(b);
    (a.max(b) + 0.05) / (a.min(b) + 0.05)
}
/// Slint generates a distinct global type per app; this shares its application code.
#[macro_export]
macro_rules! apply_theme {
    ($window:expr,$global:ty,$theme:expr) => {{
        use slint::ComponentHandle as _;
        let value = $theme;
        let global = $window.global::<$global>();
        global.set_dark(value.dark);
        global.set_reduce_motion(value.reduced);
        global.set_high_contrast(value.high_contrast);
        global.set_font_family(value.font.clone().into());
        global.set_text_scale(value.scale);
        global.set_action_primary(value.accent);
        global.set_action_primary_hover(value.hover);
        global.set_action_primary_pressed(value.pressed);
        global.set_content_on_action(value.content);
        global.set_focus_ring(value.accent);
    }};
}
/// File reads and solar/theme calculation run off the UI thread. The last good
/// theme is retained on parse/IO errors; no user preferences are overwritten.
pub fn watch_appearance(
    mut update: impl FnMut(ThemeValues) + Send + 'static,
) -> std::io::Result<AppearanceWatch> {
    let path = Appearance::path()?;
    let (send, receive) = std::sync::mpsc::channel();
    let thread = std::thread::Builder::new()
        .name("daisy-appearance".into())
        .spawn(move || {
            let mut previous = None;
            let mut minute = 0;
            loop {
                let modified = std::fs::metadata(&path)
                    .ok()
                    .and_then(|m| m.modified().ok());
                let current_minute = std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .unwrap_or_default()
                    .as_secs()
                    / 60;
                if previous != modified || minute != current_minute {
                    match Appearance::load(&path) {
                        Ok(value) => update(ThemeValues::from_appearance(&value)),
                        Err(error) => eprintln!("[appearance] {error}"),
                    };
                    previous = modified;
                    minute = current_minute;
                }
                if receive
                    .recv_timeout(std::time::Duration::from_millis(500))
                    .is_ok()
                {
                    break;
                }
            }
        })?;
    Ok(AppearanceWatch {
        stop: send,
        thread: Some(thread),
    })
}
pub struct AppearanceWatch {
    stop: std::sync::mpsc::Sender<()>,
    thread: Option<std::thread::JoinHandle<()>>,
}
impl Drop for AppearanceWatch {
    fn drop(&mut self) {
        let _ = self.stop.send(());
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}
