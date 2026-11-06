use std::{env, path::PathBuf, process::Command};
fn run(program: &str, args: &[&str]) {
    assert!(
        Command::new(program)
            .args(args)
            .status()
            .expect(program)
            .success(),
        "{program} failed"
    );
}
fn main() {
    let out = PathBuf::from(env::var_os("OUT_DIR").unwrap());
    let out = out.to_str().unwrap();
    let xml = "../daisy_idle_monitor/ext-idle-notify-v1.xml";
    println!("cargo:rerun-if-changed={xml}");
    println!("cargo:rerun-if-changed=wayland_shim.c");
    run(
        "wayland-scanner",
        &["client-header", xml, &format!("{out}/idle.h")],
    );
    run(
        "wayland-scanner",
        &["private-code", xml, &format!("{out}/protocol.c")],
    );
    let flags = Command::new("pkg-config")
        .args(["--cflags", "wayland-client"])
        .output()
        .expect("pkg-config");
    assert!(
        flags.status.success(),
        "wayland-client development headers required"
    );
    for (source, object) in [
        ("wayland_shim.c".to_string(), "shim.o"),
        (format!("{out}/protocol.c"), "protocol.o"),
    ] {
        let mut cc = Command::new(env::var("CC").unwrap_or_else(|_| "cc".into()));
        cc.args([
            "-std=c11", "-Os", "-fPIC", "-Wall", "-Wextra", "-Werror", "-I", out,
        ]);
        cc.args(
            String::from_utf8(flags.stdout.clone())
                .unwrap()
                .split_whitespace(),
        );
        assert!(cc
            .args(["-c", &source, "-o", &format!("{out}/{object}")])
            .status()
            .expect("cc")
            .success());
    }
    run(
        &env::var("AR").unwrap_or_else(|_| "ar".into()),
        &[
            "crs",
            &format!("{out}/libidle_protocol.a"),
            &format!("{out}/shim.o"),
            &format!("{out}/protocol.o"),
        ],
    );
    println!("cargo:rustc-link-search=native={out}");
    println!("cargo:rustc-link-lib=static=idle_protocol");
    println!("cargo:rustc-link-lib=wayland-client");
    println!("cargo:rustc-link-lib=pthread");
    println!("cargo:rustc-link-lib=c");
}
