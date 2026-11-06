use daisy_core::{appearance::Appearance, calculator::Calculator};
use slint::ComponentHandle;
use std::{cell::RefCell, rc::Rc};
slint::include_modules!();
fn refresh(ui: &CalculatorWindow, state: &Calculator) {
    let display = if state.display == "Error" {
        daisy_core::appearance::message(ui.get_language().as_str(), "Error")
    } else {
        state.display.clone()
    };
    ui.set_display(display.into());
    ui.set_expression(state.expression.clone().into());
    ui.set_scientific(state.scientific);
    ui.set_inverse(state.inverse);
    ui.set_hyp(state.hyp);
    ui.set_memory(state.has_memory);
    ui.set_angle(format!("{:?}", state.angle).to_uppercase().into());
}
fn localize(ui: &CalculatorWindow, language: &str) {
    let was_error = ui.get_display().as_str()
        == daisy_core::appearance::message(ui.get_language().as_str(), "Error");
    let text = |s| daisy_core::appearance::message(language, s).into();
    ui.set_language(language.into());
    ui.set_title_text(text("Calculator"));
    ui.set_standard_label(text("Standard"));
    ui.set_scientific_label(text("Scientific"));
    ui.set_toggle_label(text("Toggle Standard / Scientific"));
    if was_error {
        ui.set_display(text("Error"));
    }
}
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.first().map(String::as_str) == Some("--evaluate") {
        let mut state = Calculator::default();
        for key in args.iter().skip(1) {
            state.press(key);
        }
        println!("{}", serde_json::to_string(&state)?);
        return Ok(());
    }
    daisy_platform::initialize("org.daisyos.calculator")?;
    let ui = CalculatorWindow::new()?;
    let appearance = Appearance::load(&Appearance::path()?).unwrap_or_else(|e| {
        eprintln!("[appearance] {e}");
        Appearance::default()
    });
    let initial_theme = daisy_ui::ThemeValues::from_appearance(&appearance);
    localize(&ui, &initial_theme.language);
    daisy_ui::apply_theme!(ui, DaisyTheme, initial_theme);
    let mut initial = Calculator::default();
    initial.scientific = args.iter().any(|a| a == "--scientific");
    refresh(&ui, &initial);
    let state = Rc::new(RefCell::new(initial));
    let weak = ui.as_weak();
    let model = state.clone();
    ui.on_command(move |key| {
        let mut model = model.borrow_mut();
        model.press(key.as_str());
        if let Some(ui) = weak.upgrade() {
            refresh(&ui, &model);
        }
    });
    let weak = ui.as_weak();
    let _appearance_watch = daisy_ui::watch_appearance(move |theme| {
        let _ = weak.upgrade_in_event_loop(move |ui| {
            localize(&ui, &theme.language);
            daisy_ui::apply_theme!(ui, DaisyTheme, theme);
        });
    })?;
    ui.show()?;
    let blur = daisy_platform::blur::attach(&ui);
    let failed = daisy_platform::diagnostics(&ui, &args)?;
    ui.run()?;
    if failed.get() {
        return Err("Snapshot failed".into());
    }
    if args.iter().any(|a| a == "--require-blur") && !blur.ready.get() {
        return Err("Compositor blur was not confirmed".into());
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
    fn pointer_then_keyboard_keeps_calculator_shortcuts() {
        i_slint_backend_testing::init_no_event_loop();
        let ui = CalculatorWindow::new().unwrap();
        ui.window().set_size(LogicalSize::new(360., 560.));
        ui.show().unwrap();
        let state = Rc::new(RefCell::new(Calculator::default()));
        let model = state.clone();
        let weak = ui.as_weak();
        ui.on_command(move |key| {
            model.borrow_mut().press(key.as_str());
            refresh(&weak.upgrade().unwrap(), &model.borrow());
        });
        ui.window()
            .dispatch_event(WindowEvent::WindowActiveChanged(true));
        let p = LogicalPosition::new(50., 260.);
        ui.window().dispatch_event(WindowEvent::PointerPressed {
            position: p,
            button: PointerEventButton::Left,
        });
        ui.window().dispatch_event(WindowEvent::PointerReleased {
            position: p,
            button: PointerEventButton::Left,
        });
        assert_eq!(
            ui.get_display().as_str(),
            "7",
            "Pointer must hit the actual digit key"
        );
        for key in ["2", "+", "3", "\n"] {
            ui.window()
                .dispatch_event(WindowEvent::KeyPressed { text: key.into() });
            ui.window()
                .dispatch_event(WindowEvent::KeyReleased { text: key.into() });
        }
        assert_eq!(
            ui.get_display().as_str(),
            "75",
            "Enter must evaluate after a button has focus"
        );
        ui.window().dispatch_event(WindowEvent::KeyPressed {
            text: slint::platform::Key::Escape.into(),
        });
        assert_eq!(ui.get_display().as_str(), "0");
    }
}
