use zbus::{export::futures_core::Stream, interface, zvariant::ObjectPath};

struct RequestPortal;
#[interface(name = "org.freedesktop.impl.portal.Request")]
impl RequestPortal {
    async fn close(&self) {
        println!("Request closed!");
    }
}

#[tokio::main(flavor = "current_thread")]
async fn main() -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let conn = zbus::connection::Builder::session()?
        .name("org.test.Portal")?
        .build()
        .await?;

    let path = ObjectPath::try_from("/org/freedesktop/portal/desktop/request/test_0")?;
    conn.object_server().at(&path, RequestPortal).await?;
    println!("Ready");

    let mut stream = zbus::MessageStream::from(&conn);
    use std::future::poll_fn;
    while let Some(Ok(msg)) = poll_fn(|cx| std::pin::Pin::new(&mut stream).poll_next(cx)).await {
        println!("Got msg: header={:?}", msg.header());
    }
    Ok(())
}
