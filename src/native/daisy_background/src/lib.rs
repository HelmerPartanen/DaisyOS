//! Runtime-free idle monitor. Only generated Wayland inline adapters remain C.
#![no_std]
use core::{
    ffi::{c_char, c_int, c_void},
    ptr,
};
type Object = *mut c_void;
type Callback = Option<unsafe extern "C" fn(Object)>;
extern "C" {
    fn wl_display_connect(name: *const c_char) -> Object;
    fn wl_display_disconnect(display: Object);
    fn wl_display_roundtrip(display: Object) -> c_int;
    fn wl_display_flush(display: Object) -> c_int;
    fn wl_display_get_fd(display: Object) -> c_int;
    fn wl_display_prepare_read(display: Object) -> c_int;
    fn wl_display_cancel_read(display: Object);
    fn wl_display_read_events(display: Object) -> c_int;
    fn wl_display_dispatch_pending(display: Object) -> c_int;
    fn daisy_registry(display: Object) -> Object;
    fn daisy_registry_listen(
        registry: Object,
        listener: *const RegistryListener,
        data: Object,
    ) -> c_int;
    fn daisy_bind(registry: Object, id: u32, seat: c_int) -> Object;
    fn daisy_notification(notifier: Object, timeout: u32, seat: Object) -> Object;
    fn daisy_notification_listen(
        notification: Object,
        listener: *const IdleListener,
        data: Object,
    ) -> c_int;
    fn daisy_destroy_notification(notification: Object);
    fn daisy_destroy_notifier(notifier: Object);
    fn daisy_destroy_seat(seat: Object);
    fn daisy_destroy_registry(registry: Object);
}
#[panic_handler]
fn panic(_: &core::panic::PanicInfo) -> ! {
    unsafe { libc::abort() }
}
#[repr(C)]
struct RegistryListener {
    global: unsafe extern "C" fn(Object, Object, u32, *const c_char, u32),
    removed: unsafe extern "C" fn(Object, Object, u32),
}
#[repr(C)]
struct IdleListener {
    idle: unsafe extern "C" fn(Object, Object),
    resumed: unsafe extern "C" fn(Object, Object),
}
struct State {
    display: Object,
    registry: Object,
    notifier: Object,
    seat: Object,
    notification: Object,
    idle: Callback,
    resumed: Callback,
    data: Object,
}
impl State {
    fn new(idle: Callback, resumed: Callback, data: Object) -> Self {
        Self {
            display: ptr::null_mut(),
            registry: ptr::null_mut(),
            notifier: ptr::null_mut(),
            seat: ptr::null_mut(),
            notification: ptr::null_mut(),
            idle,
            resumed,
            data,
        }
    }
    // State stays at a stable address until every proxy has been destroyed.
    unsafe fn connect(&mut self) -> bool {
        self.display = wl_display_connect(ptr::null());
        if self.display.is_null() {
            return false;
        }
        self.registry = daisy_registry(self.display);
        if self.registry.is_null() {
            return false;
        }
        if daisy_registry_listen(
            self.registry,
            &REGISTRY_LISTENER,
            (self as *mut Self).cast(),
        ) < 0
        {
            return false;
        }
        wl_display_roundtrip(self.display) >= 0 && !self.notifier.is_null() && !self.seat.is_null()
    }
    unsafe fn run(&mut self, timeout: u32, wake: c_int) -> bool {
        if !self.connect() {
            return false;
        }
        self.notification = daisy_notification(self.notifier, timeout, self.seat);
        if self.notification.is_null()
            || daisy_notification_listen(
                self.notification,
                &IDLE_LISTENER,
                (self as *mut Self).cast(),
            ) < 0
        {
            return false;
        }
        let mut fds = [
            libc::pollfd {
                fd: wl_display_get_fd(self.display),
                events: libc::POLLIN,
                revents: 0,
            },
            libc::pollfd {
                fd: wake,
                events: libc::POLLIN,
                revents: 0,
            },
        ];
        loop {
            while wl_display_prepare_read(self.display) != 0 {
                if wl_display_dispatch_pending(self.display) < 0 {
                    return false;
                }
            }
            let flush = wl_display_flush(self.display);
            let blocked = flush < 0 && *libc::__errno_location() == libc::EAGAIN;
            if flush < 0 && !blocked {
                wl_display_cancel_read(self.display);
                return false;
            }
            fds[0].events = libc::POLLIN | if blocked { libc::POLLOUT } else { 0 };
            let result = libc::poll(fds.as_mut_ptr(), 2, -1);
            if result < 0 {
                wl_display_cancel_read(self.display);
                if *libc::__errno_location() == libc::EINTR {
                    continue;
                }
                return false;
            }
            if fds[1].revents != 0 {
                wl_display_cancel_read(self.display);
                // Standalone stdin accepts bytes until EOF, matching the C tool.
                if wake == 0 && fds[1].revents & libc::POLLIN != 0 {
                    let mut bytes = [0u8; 64];
                    if libc::read(0, bytes.as_mut_ptr().cast(), bytes.len()) > 0 {
                        continue;
                    }
                }
                return true;
            }
            if fds[0].revents & (libc::POLLERR | libc::POLLHUP | libc::POLLNVAL) != 0 {
                wl_display_cancel_read(self.display);
                return false;
            }
            if fds[0].revents & libc::POLLIN != 0 {
                if wl_display_read_events(self.display) < 0 {
                    return false;
                }
                if wl_display_dispatch_pending(self.display) < 0 {
                    return false;
                }
            } else {
                wl_display_cancel_read(self.display);
            }
        }
    }
}
impl Drop for State {
    fn drop(&mut self) {
        unsafe {
            if !self.notification.is_null() {
                daisy_destroy_notification(self.notification);
            }
            if !self.seat.is_null() {
                daisy_destroy_seat(self.seat);
            }
            if !self.notifier.is_null() {
                daisy_destroy_notifier(self.notifier);
            }
            if !self.registry.is_null() {
                daisy_destroy_registry(self.registry);
            }
            if !self.display.is_null() {
                wl_display_disconnect(self.display);
            }
        }
    }
}
unsafe extern "C" fn global(
    data: Object,
    registry: Object,
    id: u32,
    interface: *const c_char,
    _: u32,
) {
    let s = &mut *data.cast::<State>();
    if libc::strcmp(interface, c"wl_seat".as_ptr()) == 0 && s.seat.is_null() {
        s.seat = daisy_bind(registry, id, 1);
    }
    if libc::strcmp(interface, c"ext_idle_notifier_v1".as_ptr()) == 0 && s.notifier.is_null() {
        s.notifier = daisy_bind(registry, id, 0);
    }
}
unsafe extern "C" fn removed(_: Object, _: Object, _: u32) {}
unsafe extern "C" fn idle(data: Object, _: Object) {
    let s = &*data.cast::<State>();
    if let Some(f) = s.idle {
        f(s.data);
    }
}
unsafe extern "C" fn resumed(data: Object, _: Object) {
    let s = &*data.cast::<State>();
    if let Some(f) = s.resumed {
        f(s.data);
    }
}
static REGISTRY_LISTENER: RegistryListener = RegistryListener { global, removed };
static IDLE_LISTENER: IdleListener = IdleListener { idle, resumed };
struct Monitor {
    thread: libc::pthread_t,
    pipe: [c_int; 2],
    timeout: u32,
    idle: Callback,
    resumed: Callback,
    data: Object,
}
extern "C" fn worker(data: Object) -> Object {
    unsafe {
        let m = &*data.cast::<Monitor>();
        let mut state = State::new(m.idle, m.resumed, m.data);
        if !state.run(m.timeout, m.pipe[0]) {
            let message = b"[daisy-idle] Wayland idle monitor disconnected or unavailable\n";
            libc::write(2, message.as_ptr().cast(), message.len());
        }
    }
    ptr::null_mut()
}
#[no_mangle]
pub extern "C" fn daisy_idle_monitor_is_available() -> c_int {
    let mut state = State::new(None, None, ptr::null_mut());
    unsafe { c_int::from(state.connect()) }
}
/// # Safety
/// Callbacks/data must outlive stop; callbacks must not stop their own monitor.
#[no_mangle]
pub unsafe extern "C" fn daisy_idle_monitor_start(
    timeout_ms: u32,
    on_idle: Callback,
    on_resumed: Callback,
    user_data: Object,
) -> Object {
    if timeout_ms == 0 {
        return ptr::null_mut();
    }
    let m = libc::malloc(core::mem::size_of::<Monitor>()).cast::<Monitor>();
    if m.is_null() {
        return ptr::null_mut();
    }
    ptr::write(
        m,
        Monitor {
            thread: core::mem::zeroed(),
            pipe: [-1; 2],
            timeout: timeout_ms,
            idle: on_idle,
            resumed: on_resumed,
            data: user_data,
        },
    );
    if libc::pipe2((*m).pipe.as_mut_ptr(), libc::O_CLOEXEC) != 0 {
        libc::free(m.cast());
        return ptr::null_mut();
    }
    let mut attributes = core::mem::MaybeUninit::<libc::pthread_attr_t>::uninit();
    let mut result = libc::pthread_attr_init(attributes.as_mut_ptr());
    if result == 0 {
        let attributes = attributes.assume_init_mut();
        result = libc::pthread_attr_setstacksize(attributes, 1024 * 1024);
        if result == 0 {
            result = libc::pthread_create(&mut (*m).thread, attributes, worker, m.cast());
        }
        libc::pthread_attr_destroy(attributes);
    }
    if result != 0 {
        libc::close((*m).pipe[0]);
        libc::close((*m).pipe[1]);
        libc::free(m.cast());
        return ptr::null_mut();
    }
    m.cast()
}
/// # Safety
/// Stop exactly once with a live handle from start; null is accepted.
#[no_mangle]
pub unsafe extern "C" fn daisy_idle_monitor_stop(handle: Object) {
    if handle.is_null() {
        return;
    }
    let m = &*handle.cast::<Monitor>();
    // Closing the writer wakes poll without a shared mutable flag or SIGPIPE.
    libc::close(m.pipe[1]);
    libc::pthread_join(m.thread, ptr::null_mut());
    libc::close(m.pipe[0]);
    libc::free(handle);
}
unsafe extern "C" fn print_idle(_: Object) {
    libc::write(1, c"IDLE\n".as_ptr().cast(), 5);
}
unsafe extern "C" fn print_resumed(_: Object) {
    libc::write(1, c"RESUMED\n".as_ptr().cast(), 8);
}
/// # Safety
/// argv must be the process arguments supplied by libc.
pub unsafe fn standalone(argc: c_int, argv: *const *const c_char) -> c_int {
    let mut seconds = 600u32;
    if argc > 1 {
        let mut end = ptr::null_mut();
        let value = libc::strtoul(*argv.add(1), &mut end, 10);
        if value > 0 && *end == 0 {
            if value > (u32::MAX / 1000) as libc::c_ulong {
                return 2;
            }
            seconds = value as u32;
        }
    }
    let mut state = State::new(Some(print_idle), Some(print_resumed), ptr::null_mut());
    c_int::from(!state.run(seconds * 1000, 0))
}
