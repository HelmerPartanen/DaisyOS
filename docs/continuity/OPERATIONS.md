# Continuity operations status

This checkout contains an audit, experimental protocol and isolated core rules.
There is no continuityd executable, systemd unit, Settings page, pairing flow or
live Continuity sharing. Do not open firewall ports or install a placeholder unit.
Existing LocalSend remains independently available with its own receive consent.

For future implementation, keep Continuity disabled and invisible until explicit
opt-in in the active graphical session. Service failure must leave normal local
clipboard, Files, login and input usable. Run unprivileged and independently of
Settings, with bounded restart policy, no busy polling and cancellation on logout.

Future diagnostics must distinguish: disabled service; no active session; Avahi
unavailable; multicast blocked; hostname resolution failure; unicast UDP blocked;
peer asleep; incompatible protocol; changed identity; permission denied. Explain
that Wi-Fi and Ethernet can interoperate through a LAN bridge, while guest Wi-Fi,
AP isolation and VLAN boundaries may prevent reachability. Do not require internet,
port forwarding or firewall disablement. mDNS UDP 5353 is separate from the
proposed QUIC UDP 47621; actual bound port is advertised.

Backup guidance: never clone private device identities or blindly restore peer
trust onto another installation. Identity reset requires new pairing. Record
schema migrations and revoke recovery must be atomic. Unknown future formats
must fail closed without erasing evidence or accepting grants.

See [TEST_MATRIX.md](TEST_MATRIX.md) for development commands, evidence and
release gates; [REPO_INTEGRATION.md](REPO_INTEGRATION.md) for actual source paths.
