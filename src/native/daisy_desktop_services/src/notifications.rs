//! Routes sandbox notifications to DaisyOS's existing notification center.
//! Mirrors `NotificationPortal.cs`.

use std::{
    collections::HashMap,
    sync::{Arc, Mutex},
};
use zbus::{
    export::futures_core::Stream,
    zvariant::{OwnedValue, Value},
};

struct Item {
    app: String,
    id: String,
    actions: HashMap<String, (String, Option<OwnedValue>)>,
}

#[derive(Clone, Default)]
pub struct NotificationPortal {
    items: Arc<Mutex<HashMap<u32, Item>>>,
}

impl NotificationPortal {
    pub fn start_action_listener(&self, connection: zbus::Connection) {
        let items = self.items.clone();
        tokio::spawn(async move {
            let Ok(rule) = zbus::MatchRule::builder()
                .msg_type(zbus::message::Type::Signal)
                .sender("org.freedesktop.Notifications")
                .and_then(|b| b.path("/org/freedesktop/Notifications"))
                .and_then(|b| b.interface("org.freedesktop.Notifications"))
                .and_then(|b| b.member("ActionInvoked"))
                .map(|b| b.build())
            else {
                return;
            };
            let Ok(mut stream) = zbus::MessageStream::for_match_rule(rule, &connection, None).await
            else {
                return;
            };
            use std::future::poll_fn;
            while let Some(Ok(msg)) =
                poll_fn(|cx| std::pin::Pin::new(&mut stream).poll_next(cx)).await
            {
                let Ok((nid, action_key)) = msg.body().deserialize::<(u32, String)>() else {
                    continue;
                };
                let found = {
                    let guard = items.lock().unwrap();
                    guard.get(&nid).and_then(|item| {
                        item.actions.get(&action_key).map(|(act, target)| {
                            (item.app.clone(), item.id.clone(), act.clone(), target.clone())
                        })
                    })
                };
                if let Some((app, id, action, target)) = found {
                    let target_vec: Vec<Value<'_>> = target.into_iter().map(Value::from).collect();
                    let _ = connection
                        .emit_signal(
                            None::<&str>,
                            "/org/freedesktop/portal/desktop",
                            "org.freedesktop.impl.portal.Notification",
                            "ActionInvoked",
                            &(app.as_str(), id.as_str(), action.as_str(), target_vec),
                        )
                        .await;
                }
            }
        });
    }

    pub async fn add(
        &self,
        connection: &zbus::Connection,
        app: &str,
        id: &str,
        data: &HashMap<String, OwnedValue>,
    ) -> Result<(), zbus::Error> {
        let previous = {
            let guard = self.items.lock().unwrap();
            guard
                .iter()
                .find(|(_, item)| item.app == app && item.id == id)
                .map(|(k, _)| *k)
                .unwrap_or(0)
        };

        let mut actions: Vec<String> = Vec::new();
        let mut map: HashMap<String, (String, Option<OwnedValue>)> = HashMap::new();

        if let Some(default_action) = data.get("default-action").and_then(|v| <&str>::try_from(v).ok())
        {
            actions.push("default".into());
            actions.push("Open".into());
            let target = data.get("default-action-target").and_then(|v| v.try_clone().ok());
            map.insert("default".into(), (default_action.to_string(), target));
        }

        if let Some(Value::Array(buttons)) = data.get("buttons").map(|v| &**v) {
            for (i, item) in buttons.iter().enumerate() {
                let Value::Dict(button_dict) = item else {
                    continue;
                };
                let Ok(Some(action)) = button_dict.get::<_, &str>(&"action") else {
                    continue;
                };
                let Ok(Some(label)) = button_dict.get::<_, &str>(&"label") else {
                    continue;
                };
                let key = format!("button-{i}");
                actions.push(key.clone());
                actions.push(label.to_string());
                let target = button_dict
                    .get::<_, Value<'_>>(&"target")
                    .ok()
                    .flatten()
                    .and_then(|v| OwnedValue::try_from(v).ok());
                map.insert(key, (action.to_string(), target));
            }
        }

        let title = data
            .get("title")
            .and_then(|v| <&str>::try_from(v).ok())
            .unwrap_or(app);
        let body = data
            .get("body")
            .and_then(|v| <&str>::try_from(v).ok())
            .unwrap_or("");

        let mut hints: HashMap<&str, Value<'_>> = HashMap::new();
        hints.insert("desktop-entry", Value::from(app));

        let reply = connection
            .call_method(
                Some("org.freedesktop.Notifications"),
                "/org/freedesktop/Notifications",
                Some("org.freedesktop.Notifications"),
                "Notify",
                &(app, previous, "", title, body, actions, hints, -1i32),
            )
            .await?;

        let result: u32 = reply.body().deserialize()?;
        let mut guard = self.items.lock().unwrap();
        if previous != 0 {
            guard.remove(&previous);
        }
        guard.insert(
            result,
            Item {
                app: app.to_string(),
                id: id.to_string(),
                actions: map,
            },
        );
        Ok(())
    }

    pub async fn remove(
        &self,
        connection: &zbus::Connection,
        app: &str,
        id: &str,
    ) -> Result<(), zbus::Error> {
        let previous = {
            let mut guard = self.items.lock().unwrap();
            let found = guard
                .iter()
                .find(|(_, item)| item.app == app && item.id == id)
                .map(|(k, _)| *k)
                .unwrap_or(0);
            if found != 0 {
                guard.remove(&found);
            }
            found
        };
        if previous == 0 {
            return Ok(());
        }
        connection
            .call_method(
                Some("org.freedesktop.Notifications"),
                "/org/freedesktop/Notifications",
                Some("org.freedesktop.Notifications"),
                "CloseNotification",
                &(previous,),
            )
            .await?;
        Ok(())
    }
}
