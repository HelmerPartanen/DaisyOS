# Enable DaisyOS Mail browser sign-in

This is a one-time task for the DaisyOS distributor. Ordinary Mail users only
choose a provider and sign in in their browser. A source build cannot generate
Google or Microsoft application identities locally.

## Google

1. Use a Google Cloud project owned by the DaisyOS distributor and configure its
   consent screen for DaisyOS Mail.
2. Create an OAuth client with application type **Desktop app**. Keep its client ID
   and the desktop client secret issued with it.
3. Configure `openid`, `email`, `profile`, and `https://mail.google.com/` access.
   During development, allow the intended test accounts. Complete the applicable
   Google verification before public distribution.

Follow [Google’s desktop authorization guide](https://developers.google.com/identity/protocols/oauth2/native-app).
Mail uses PKCE and a temporary `127.0.0.1` loopback callback for Google.

## Microsoft

1. In a tenant you administer, register **DaisyOS Mail** in Microsoft Entra.
2. To support Outlook.com and school/work accounts, choose support for both
   organizational directories and personal Microsoft accounts.
3. Configure a public desktop client with the system-browser redirect
   `http://localhost/`. Mail selects a temporary local port; localhost registration
   permits that port to vary.
4. Allow delegated Exchange Online `IMAP.AccessAsUser.All` and `SMTP.Send`, plus
   the OpenID/offline-access scopes listed in [Mail.md](Mail.md). Tenant policy must
   permit these protocols and any required consent.
5. Keep the application’s client ID. Mail’s public Microsoft client does not use a
   client secret.

See [Microsoft’s registration guide](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app)
and [redirect rules](https://learn.microsoft.com/en-us/entra/identity-platform/reply-url).

## Provision the registration

Copy [the registration example](../packaging/mail/mail-providers.example.json) to
`~/.config/daisyos/mail/providers.json`, or install a system-wide file at
`/etc/daisyos/mail-providers.json`. Populate the three fields with the actual
registration values. Keep a private local file user-readable only. No IDs or
configuration fields appear in Mail’s account setup UI.

Mail loads registration from, in order:

1. `mail-providers.json` beside the Mail executable.
2. `/etc/daisyos/mail-providers.json`.
3. `$XDG_CONFIG_HOME/daisyos/mail/providers.json` (default `~/.config`).

Later files replace earlier files. `DAISYOS_MAIL_PROVIDER_CONFIG` selects an
explicit file instead. The existing individual `DAISYOS_MAIL_*` environment values
can override file values.

To package an application-owned registration with Mail:

```sh
dotnet publish src/DaisyOS.Mail -c Release \
  -p:DaisyMailProviderConfig=/path/to/mail-providers.json
```

This copies the registration beside the executable in build and publish output.
These are desktop application registration values, not user mailbox tokens.
Refresh tokens remain in the system keyring. Desktop clients are public clients;
never use a confidential web-server application secret here.

Check a provisioned installation without opening a browser:

```sh
daisyos-mail --check-sign-in
```

Exit code `0` means both registrations have valid configuration shapes; `2` means
one is missing, malformed, or unreadable. This does not prove provider approval,
consent, or live mailbox access. Close and reopen Mail after provisioning, then finish a real browser sign-in to verify those.

## Install the desktop application

```sh
scripts/install-mail-app.sh /path/to/DaisyOS.Mail --pin
```

This installs the desktop entry and icons, registers the `mailto:` handler without
replacing a different chosen default, and pins Mail through the active desktop’s
settings service. If no desktop is reachable, the pin is saved for the next
session. The standard system-app installer also installs Mail. It is included in
the built-in launcher catalog and the default dock for new sessions.
