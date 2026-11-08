//! PolicyKit authentication agent supervisor.
//! Runs the lightweight `daisyos-polkit-agent` C helper and delegates only interactive
//! authentication prompts to the on-demand Avalonia UI helper.

use std::{path::PathBuf, process::Stdio, time::Duration};
use tokio::{
    io::{AsyncBufReadExt, AsyncWriteExt, BufReader},
    process::{Child, Command},
};

pub async fn run_supervisor(helper_dir: PathBuf, ui_binary: PathBuf) {
    let agent_path = helper_dir.join("daisyos-polkit-agent");
    loop {
        if let Err(err) = supervise_once(&agent_path, &ui_binary).await {
            eprintln!(
                "[authentication] Agent unavailable; portals remain active. Retrying in 30 seconds: {err}"
            );
        }
        tokio::time::sleep(Duration::from_secs(30)).await;
    }
}

async fn supervise_once(
    agent_path: & std::path::Path,
    ui_binary: & std::path::Path,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let mut agent = Command::new(agent_path)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true)
        .spawn()?;

    let mut agent_in = agent.stdin.take().ok_or("Missing agent stdin")?;
    let agent_out = agent.stdout.take().ok_or("Missing agent stdout")?;
    let agent_err = agent.stderr.take().ok_or("Missing agent stderr")?;

    tokio::spawn(async move {
        let mut reader = BufReader::new(agent_err).lines();
        while let Ok(Some(line)) = reader.next_line().await {
            eprintln!("[authentication] {line}");
        }
    });

    let mut lines = BufReader::new(agent_out).lines();
    let first = tokio::time::timeout(Duration::from_secs(10), lines.next_line())
        .await
        .map_err(|_| "Authentication agent registration timed out.")??
        .ok_or("Authentication agent could not register.")?;

    if !first.starts_with("registered\t") && first != "registered" {
        return Err(format!("Unexpected agent greeting: {first}").into());
    }

    let mut pending_lines: Vec<String> = Vec::new();
    let mut ui_child: Option<Child> = None;
    let mut ui_stdin: Option<tokio::process::ChildStdin> = None;
    let (ui_tx, mut ui_rx) = tokio::sync::mpsc::unbounded_channel::<String>();

    loop {
        tokio::select! {
            line_res = lines.next_line() => {
                let Some(line) = line_res? else {
                    break;
                };
                let kind = line.split('\t').next().unwrap_or("");
                match kind {
                    "begin" => {
                        if let Some(mut prev) = ui_child.take() {
                            let _ = prev.kill().await;
                        }
                        ui_stdin = None;
                        pending_lines.clear();
                        pending_lines.push(line);
                    }
                    "action" | "identity" => {
                        pending_lines.push(line);
                    }
                    "ready" => {
                        pending_lines.push(line);
                        if let Ok(mut child) = Command::new(ui_binary)
                            .arg("--polkit-prompt-ui")
                            .stdin(Stdio::piped())
                            .stdout(Stdio::piped())
                            .stderr(Stdio::inherit())
                            .kill_on_drop(true)
                            .spawn()
                        {
                            let mut sin = child.stdin.take().unwrap();
                            let sout = child.stdout.take().unwrap();
                            for queued in pending_lines.drain(..) {
                                let _ = sin.write_all(queued.as_bytes()).await;
                                let _ = sin.write_all(b"\n").await;
                            }
                            let _ = sin.flush().await;
                            ui_stdin = Some(sin);
                            ui_child = Some(child);
                            let tx = ui_tx.clone();
                            tokio::spawn(async move {
                                let mut out_lines = BufReader::new(sout).lines();
                                while let Ok(Some(reply)) = out_lines.next_line().await {
                                    if tx.send(reply).is_err() {
                                        break;
                                    }
                                }
                                let _ = tx.send("cancel\t".to_string());
                            });
                        } else {
                            pending_lines.clear();
                            let _ = agent_in.write_all(b"cancel\t\n").await;
                            let _ = agent_in.flush().await;
                        }
                    }
                    "password" | "echo" | "info" => {
                        if let Some(ref mut sin) = ui_stdin {
                            let _ = sin.write_all(line.as_bytes()).await;
                            let _ = sin.write_all(b"\n").await;
                            let _ = sin.flush().await;
                        }
                    }
                    "success" | "failed" | "cancelled" => {
                        pending_lines.clear();
                        if let Some(ref mut sin) = ui_stdin {
                            let _ = sin.write_all(line.as_bytes()).await;
                            let _ = sin.write_all(b"\n").await;
                            let _ = sin.flush().await;
                        }
                        ui_stdin = None;
                        if let Some(mut child) = ui_child.take() {
                            tokio::spawn(async move {
                                let _ = tokio::time::timeout(Duration::from_secs(2), child.wait()).await;
                                let _ = child.kill().await;
                            });
                        }
                    }
                    _ => {}
                }
            }
            Some(reply) = ui_rx.recv() => {
                if ui_child.is_some() {
                    let _ = agent_in.write_all(reply.as_bytes()).await;
                    let _ = agent_in.write_all(b"\n").await;
                    let _ = agent_in.flush().await;
                    if reply.starts_with("cancel") {
                        ui_stdin = None;
                        if let Some(mut child) = ui_child.take() {
                            let _ = child.kill().await;
                        }
                    }
                }
            }
        }
    }

    if let Some(mut child) = ui_child.take() {
        let _ = child.kill().await;
    }
    let _ = agent.wait().await;
    Err("Authentication agent stopped.".into())
}
