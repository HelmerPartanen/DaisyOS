use daisy_core::appearance::Appearance;
use slint::ComponentHandle;
slint::include_modules!();
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    daisy_platform::initialize("org.daisyos.uikit")?;
    let ui = DaisyGallery::new()?;
    let settings = Appearance::load(&Appearance::path()?).unwrap_or_else(|error| {
        eprintln!("[appearance] {error}");
        Appearance::default()
    });
    daisy_ui::apply_theme!(
        ui,
        DaisyTheme,
        daisy_ui::ThemeValues::from_appearance(&settings)
    );
    let weak = ui.as_weak();
    ui.on_preview_theme(move |dark| {
        let mut preview = settings.clone();
        preview.document["themeMode"] = serde_json::json!(if dark { 1 } else { 0 });
        if let Some(ui) = weak.upgrade() {
            daisy_ui::apply_theme!(
                ui,
                DaisyTheme,
                daisy_ui::ThemeValues::from_appearance(&preview)
            );
        }
    });
    // Theme switch is an explicit local gallery preview; it never edits preferences.
    ui.show()?;
    let failed = daisy_platform::diagnostics(&ui, &args)?;
    ui.run()?;
    if failed.get() {
        return Err("Snapshot failed".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use slint::{
        platform::{PointerEventButton, WindowEvent},
        LogicalPosition, LogicalSize,
    };
    #[test]
    fn interactive_components_are_not_static_previews() {
        i_slint_backend_testing::init_no_event_loop();
        let ui = DaisyGallery::new().unwrap();
        ui.window().set_size(LogicalSize::new(1120., 760.));
        ui.show().unwrap();
        let p = LogicalPosition::new(1055., 56.);
        let initial = ui.global::<DaisyTheme>().get_dark();
        ui.window().dispatch_event(WindowEvent::PointerPressed {
            position: p,
            button: PointerEventButton::Left,
        });
        ui.window().dispatch_event(WindowEvent::PointerReleased {
            position: p,
            button: PointerEventButton::Left,
        });
        assert_ne!(
            ui.global::<DaisyTheme>().get_dark(),
            initial,
            "Theme switch must toggle"
        );
        ui.window()
            .dispatch_event(WindowEvent::KeyPressed { text: " ".into() });
        assert_eq!(
            ui.global::<DaisyTheme>().get_dark(),
            initial,
            "Switch must support Space with focus"
        );
    }
}
