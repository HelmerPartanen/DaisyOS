//! Additional protocol adapter for KWin's ext_background_effect_manager_v1.
//! The display is borrowed from winit; no extra compositor connection is opened.
use raw_window_handle::{HasDisplayHandle, HasWindowHandle, RawDisplayHandle, RawWindowHandle};
use slint::{winit_030::WinitWindowAccessor, ComponentHandle};
use std::{
    cell::{Cell, RefCell},
    rc::Rc,
};
use wayland_client::{
    delegate_noop,
    protocol::{wl_callback, wl_compositor, wl_registry, wl_surface},
    Connection, Dispatch, Proxy, QueueHandle,
};
use wayland_protocols::ext::background_effect::v1::client::{
    ext_background_effect_manager_v1 as manager, ext_background_effect_surface_v1 as effect,
};
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rect {
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}
/// Same inward-rounded scanline construction as KWinBlur.AddRoundedRegion.
pub fn rounded_region(rect: Rect, radius: i32) -> Vec<Rect> {
    if rect.width <= 0 || rect.height <= 0 {
        return vec![];
    }
    let radius = radius.clamp(0, rect.width.min(rect.height) / 2);
    if radius == 0 {
        return vec![rect];
    }
    let mut result = Vec::new();
    for row in 0..radius {
        let vertical = radius as f64 - row as f64 - 0.5;
        let inset =
            (radius as f64 - ((radius * radius) as f64 - vertical * vertical).sqrt()).ceil() as i32;
        let width = rect.width - inset * 2;
        if width > 0 {
            result.push(Rect {
                x: rect.x + inset,
                y: rect.y + row,
                width,
                height: 1,
            });
            result.push(Rect {
                x: rect.x + inset,
                y: rect.y + rect.height - row - 1,
                width,
                height: 1,
            });
        }
    }
    if rect.height > radius * 2 {
        result.push(Rect {
            x: rect.x,
            y: rect.y + radius,
            width: rect.width,
            height: rect.height - radius * 2,
        });
    }
    result
}
#[derive(Default)]
struct State {
    compositor: Option<wl_compositor::WlCompositor>,
    manager: Option<manager::ExtBackgroundEffectManagerV1>,
    blur: bool,
    done: bool,
    legacy: bool,
}
impl Dispatch<wl_registry::WlRegistry, ()> for State {
    fn event(
        s: &mut Self,
        r: &wl_registry::WlRegistry,
        e: wl_registry::Event,
        _: &(),
        _: &Connection,
        q: &QueueHandle<Self>,
    ) {
        if let wl_registry::Event::Global {
            name,
            interface,
            version,
        } = e
        {
            match interface.as_str() {
                "wl_compositor" => s.compositor = Some(r.bind(name, version.min(4), q, ())),
                "ext_background_effect_manager_v1" => s.manager = Some(r.bind(name, 1, q, ())),
                "org_kde_kwin_blur_manager" => s.legacy = true,
                _ => {}
            }
        }
    }
}
impl Dispatch<manager::ExtBackgroundEffectManagerV1, ()> for State {
    fn event(
        s: &mut Self,
        _: &manager::ExtBackgroundEffectManagerV1,
        e: manager::Event,
        _: &(),
        _: &Connection,
        _: &QueueHandle<Self>,
    ) {
        if let manager::Event::Capabilities { flags } = e {
            s.blur = match flags {
                wayland_client::WEnum::Value(v) => v.contains(manager::Capability::Blur),
                wayland_client::WEnum::Unknown(v) => v & 1 != 0,
            };
        }
    }
}
impl Dispatch<wl_callback::WlCallback, ()> for State {
    fn event(
        s: &mut Self,
        _: &wl_callback::WlCallback,
        _: wl_callback::Event,
        _: &(),
        _: &Connection,
        _: &QueueHandle<Self>,
    ) {
        s.done = true;
    }
}
delegate_noop!(State:ignore wl_compositor::WlCompositor);
delegate_noop!(State:ignore effect::ExtBackgroundEffectSurfaceV1);
struct Session {
    connection: Connection,
    queue: wayland_client::EventQueue<State>,
    state: State,
    _registry: wl_registry::WlRegistry,
    surface: wl_surface::WlSurface,
    effect: Option<effect::ExtBackgroundEffectSurfaceV1>,
    last: Vec<Rect>,
}
impl Session {
    fn new(
        window: &slint::winit_030::winit::window::Window,
    ) -> Result<Self, Box<dyn std::error::Error>> {
        let (RawDisplayHandle::Wayland(display), RawWindowHandle::Wayland(surface)) = (
            window.display_handle()?.as_raw(),
            window.window_handle()?.as_raw(),
        ) else {
            return Err("Not a Wayland window".into());
        };
        // SAFETY: called inside with_winit_window; winit's backend retains the
        // display through the Slint event loop. The RAII guard below destroys
        // our guest proxies before app teardown; it never disconnects the display.
        let backend = unsafe {
            wayland_backend::client::Backend::from_foreign_display(display.display.as_ptr().cast())
        };
        let connection = Connection::from_backend(backend);
        let queue = connection.new_event_queue();
        // SAFETY: this is the live wl_surface borrowed from the same winit
        // window/connection. It is not adopted, dispatched or destroyed by us.
        let id = unsafe {
            wayland_backend::client::ObjectId::from_ptr(
                wl_surface::WlSurface::interface(),
                surface.surface.as_ptr().cast(),
            )
        }?;
        let surface = wl_surface::WlSurface::from_id(&connection, id)?;
        let registry = connection.display().get_registry(&queue.handle(), ());
        connection.display().sync(&queue.handle(), ());
        connection.flush()?;
        Ok(Self {
            connection,
            queue,
            state: State::default(),
            _registry: registry,
            surface,
            effect: None,
            last: vec![],
        })
    }
    fn update(&mut self, regions: &[Rect]) -> Result<Option<bool>, Box<dyn std::error::Error>> {
        // Winit reads the shared display; dispatch only our private queue. Never
        // roundtrip or compete with winit's prepare/read/cancel event discipline.
        self.queue.dispatch_pending(&mut self.state)?;
        self.connection.flush()?;
        if !self.state.blur {
            return Ok(None);
        }
        if self.effect.is_none() {
            self.effect = Some(
                self.state
                    .manager
                    .as_ref()
                    .ok_or("No background effect manager")?
                    .get_background_effect(&self.surface, &self.queue.handle(), ()),
            );
        }
        let changed = self.last != regions;
        if changed {
            let region = self
                .state
                .compositor
                .as_ref()
                .ok_or("No compositor")?
                .create_region(&self.queue.handle(), ());
            for r in regions.iter().filter(|r| r.width > 0 && r.height > 0) {
                region.add(r.x, r.y, r.width, r.height);
            }
            self.effect.as_ref().unwrap().set_blur_region(Some(&region));
            region.destroy();
            self.last = regions.to_vec();
            self.connection.flush()?;
        }
        Ok(Some(changed))
    }
}
// wl_region has no events but requires Dispatch for construction.
delegate_noop!(State:ignore wayland_client::protocol::wl_region::WlRegion);
impl Drop for Session {
    fn drop(&mut self) {
        if let Some(effect) = self.effect.take() {
            effect.destroy();
        }
        if let Some(manager) = self.state.manager.take() {
            manager.destroy();
        }
        let _ = self.connection.flush();
    }
}
/// App-owned registration: keep it alive until after window.run(), then drop it
/// before the Slint component/backend. All foreign-handle use stays on UI thread.
pub struct BlurRegistration {
    timer: Rc<slint::Timer>,
    session: Rc<RefCell<Option<Session>>>,
    active: Rc<Cell<bool>>,
    pub ready: Rc<Cell<bool>>,
}
impl Drop for BlurRegistration {
    fn drop(&mut self) {
        self.active.set(false);
        self.timer.stop();
        self.session.borrow_mut().take();
    }
}
pub fn attach<T: ComponentHandle + 'static>(ui: &T) -> BlurRegistration {
    let session = Rc::new(RefCell::new(None::<Session>));
    let state = session.clone();
    let ready = Rc::new(Cell::new(false));
    let status = ready.clone();
    let weak = ui.as_weak();
    let timer = Rc::new(slint::Timer::default());
    let active = Rc::new(Cell::new(true));
    let enabled = active.clone();
    let update: Rc<dyn Fn(&slint::Window)> = Rc::new(move |ui| {
        if !enabled.get() {
            return;
        }
        ui.with_winit_window(|window| {
            let mut state = state.borrow_mut();
            if state.is_none() {
                match Session::new(window) {
                    Ok(value) => *state = Some(value),
                    Err(error) => {
                        eprintln!("[kwin-blur] {error}");
                        return;
                    }
                }
            }
            let size = window.inner_size().to_logical::<f64>(window.scale_factor());
            let regions = [Rect {
                x: 0,
                y: 0,
                width: size.width.ceil() as i32,
                height: size.height.ceil() as i32,
            }];
            let session = state.as_mut().unwrap();
            match session.update(&regions) {
                Ok(Some(changed)) => {
                    if !status.replace(true) {
                        eprintln!("[kwin-blur] ext background blur enabled");
                    }
                    if changed {
                        ui.request_redraw();
                    }
                }
                Ok(None) => {
                    if session.state.done && session.state.manager.is_none() && session.state.legacy
                    {
                        window.set_blur(true);
                        status.set(true);
                        ui.request_redraw();
                    }
                }
                Err(error) => eprintln!("[kwin-blur] {error}"),
            }
        });
    });
    let on_event = update.clone();
    ui.window().on_winit_window_event(move |ui, event| {
        use slint::winit_030::winit::event::WindowEvent;
        if matches!(
            event,
            WindowEvent::Resized(_)
                | WindowEvent::ScaleFactorChanged { .. }
                | WindowEvent::RedrawRequested
        ) {
            on_event(ui);
        }
        slint::winit_030::EventResult::Propagate
    });
    let status = ready.clone();
    let attempts = Cell::new(0u32);
    let startup_active = active.clone();
    let weak_timer = Rc::downgrade(&timer);
    timer.start(
        slint::TimerMode::Repeated,
        std::time::Duration::from_millis(32),
        move || {
            if let Some(ui) = weak.upgrade() {
                update(ui.window());
            }
            attempts.set(attempts.get() + 1);
            if status.get() || attempts.get() >= 160 {
                if !status.get() {
                    startup_active.set(false);
                    eprintln!("[kwin-blur] compositor blur negotiation did not complete");
                }
                if let Some(timer) = weak_timer.upgrade() {
                    timer.stop();
                }
            }
        },
    );
    BlurRegistration {
        timer,
        session,
        active,
        ready,
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rounded_mask_stays_inside() {
        let rect = Rect {
            x: 3,
            y: 7,
            width: 100,
            height: 60,
        };
        let rows = rounded_region(rect, 14);
        assert_eq!(rows.len(), 29);
        assert!(rows
            .iter()
            .all(|r| r.x >= 3 && r.y >= 7 && r.x + r.width <= 103 && r.y + r.height <= 67));
        assert_eq!(rows[0].y, 7);
        assert!(rows[0].width < 100);
        assert_eq!(rows.last().unwrap().height, 32);
    }
    #[test]
    fn empty_mask() {
        assert!(rounded_region(
            Rect {
                x: 0,
                y: 0,
                width: 0,
                height: 2
            },
            4
        )
        .is_empty());
    }
}
