//! Normal xdg-toplevel integration. Shell layer surfaces remain in the existing
//! backend until layer roles, output affinity, focus and regional blur are ported.
pub mod blur;
use slint::winit_030::winit::platform::wayland::WindowAttributesExtWayland;
pub fn initialize(application_id: &str) -> Result<(), slint::PlatformError> {
    let id = application_id.to_owned();
    slint::BackendSelector::new()
        .backend_name("winit".into())
        .renderer_name("software".into())
        .with_winit_window_attributes_hook(move |attrs| {
            attrs
                .with_name(id.clone(), "DaisyOS")
                .with_transparent(true)
        })
        .select()
}
/// Explicit acceptance-test options. Ordinary app launches do no screenshot IO.
pub fn diagnostics<T: slint::ComponentHandle + 'static>(
    ui: &T,
    args: &[String],
) -> Result<std::rc::Rc<std::cell::Cell<bool>>, Box<dyn std::error::Error>> {
    let value = |flag: &str| -> Result<Option<String>, Box<dyn std::error::Error>> {
        match args.iter().position(|a| a == flag) {
            Some(i) => Ok(Some(args.get(i + 1).ok_or("Missing option value")?.clone())),
            None => Ok(None),
        }
    };
    let path = value("--snapshot")?;
    let delay = value("--quit-after-ms")?
        .map(|v| v.parse::<u64>())
        .transpose()?;
    let failed = std::rc::Rc::new(std::cell::Cell::new(false));
    if path.is_some() || delay.is_some() {
        let weak = ui.as_weak();
        let failure = failed.clone();
        slint::Timer::single_shot(
            std::time::Duration::from_millis(delay.unwrap_or(700)),
            move || {
                if let (Some(ui), Some(path)) = (weak.upgrade(), path) {
                    let result = (|| -> Result<(), Box<dyn std::error::Error>> {
                        let pixels = ui.window().take_snapshot()?;
                        let path = std::path::Path::new(&path);
                        if let Some(parent) = path.parent().filter(|p| !p.as_os_str().is_empty()) {
                            std::fs::create_dir_all(parent)?;
                        }
                        let file = std::fs::File::create(path)?;
                        let mut png = png::Encoder::new(file, pixels.width(), pixels.height());
                        png.set_color(png::ColorType::Rgba);
                        png.set_depth(png::BitDepth::Eight);
                        png.write_header()?.write_image_data(pixels.as_bytes())?;
                        Ok(())
                    })();
                    if let Err(error) = result {
                        failure.set(true);
                        eprintln!("[snapshot] {error}");
                    }
                }
                let _ = slint::quit_event_loop();
            },
        );
    }
    Ok(failed)
}
