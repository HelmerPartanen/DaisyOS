# DaisyOS Mail

Mail is an independent Avalonia system application using the shared DaisyOS theme,
window chrome, launcher and taskbar. Run:

```sh
dotnet run --project src/DaisyOS.Mail
```

## Connecting accounts

When no account is saved, Mail shows only an account sign-in page. Choose **Continue
with Google** or **Continue with Microsoft** and finish signing in in your system
browser. These choices support Gmail/Google Workspace and Outlook/Microsoft 365,
including school accounts hosted by either provider. The same chooser is used by
Add account after you have connected a mailbox.

Mail obtains your email address from the provider’s signed identity response;
there are no password, server, or OAuth configuration fields in the product UI.
Mail only opens the inbox after sign-in and mailbox validation succeed. Removing
the last account returns to this sign-in page. Empty mailbox illustrations and
reader instructions are not displayed. The first available message opens directly.
Existing saved IMAP accounts continue to work; new manual IMAP setup is not exposed.
Other providers are not currently available through this browser-only setup.

### Distributor configuration

Follow [the one-time registration and installation guide](Mail_Registration.md).

The distributor must register Mail’s own desktop OAuth applications and configure
the app-owned registration file, or `DAISYOS_MAIL_GOOGLE_CLIENT_ID` and
`DAISYOS_MAIL_MICROSOFT_CLIENT_ID` in the process environment. Google desktop clients that require their issued secret also need
`DAISYOS_MAIL_GOOGLE_CLIENT_SECRET`. These values are never requested from users.
No third-party application identity is shipped or borrowed. A build without its
provider registration displays a nontechnical availability message instead of
opening a website that cannot connect the account.

Google scopes: `openid email profile https://mail.google.com/`. Microsoft scopes:
`openid email profile offline_access https://outlook.office.com/IMAP.AccessAsUser.All
https://outlook.office.com/SMTP.Send`. Configure desktop loopback redirects and
consent for the intended accounts. Provider verification and organization policies
still apply. The system also needs an unlocked Secret Service keyring and
`secret-tool` from libsecret. Refresh tokens are kept in that keyring.

Sign-in uses PKCE, state and nonce validation. Identity tokens are verified against
provider RSA signing keys, including issuer, audience, lifetime and nonce checks,
before selecting an email address. Mail tests incoming and outgoing authentication
before saving the account. Live provider sign-in and delivery require registered
applications and test accounts and have not been verified by the acceptance runner.

Provider references: [Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect),
[Microsoft ID token claims](https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference),
[Microsoft OAuth for IMAP/SMTP](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth/).

## Daily use

- Select an account and a real server mailbox in the sidebar.
- The most recent 100 messages load first. Load older messages adds 100 more.
- Search filters loaded subjects, senders, addresses and previews as you type.
  Press Enter to search subjects, senders and bodies across the server mailbox.
  Load older messages expands server search results too. Unread and Starred
  filters can be combined. Server search results do not replace the inbox cache.
- F5 refreshes. A one-minute timer checks the selected mailbox. Ctrl+F focuses
  search; Ctrl+N opens a new message. `mailto:` activation pre-fills a compose window.
- Select a message to read and mark it read. Reply, Reply all, Forward, Star,
  Unread, Archive, Trash and Move perform real operations. Trash moves messages;
  Mail does not permanently expunge a mailbox. Archive/Trash require server special
  folders; use Move for a provider without those mappings.
- Compose opens inside the reading pane, with inline recipient/subject rows, a
  bounded writing area and a fixed Send/Attach footer. Extra recipients are
  optional: **Copy to** shares their addresses; **Hidden copy** keeps them private
  from the other recipients. Both are available from **Add more recipients**.
  Replies keep the original message separately, collapsed behind **Show original
  message**; the original remains part of the sent message and saved draft.
  Drafts autosave and also save when closing or switching composers. Esc closes
  the composer, Ctrl+S saves, and Ctrl+Enter sends. Opening a blank composer and
  closing it without edits does not create an empty draft. File attachments can
  be removed using their chips. The message editor owns its scroll viewport;
  stable reusable input chrome keeps focus/caret insets and surfaces fixed.
  Drafts save locally every three seconds and when closing. Drafts on this device
  lets you resume or delete them. Attachment references must remain accessible.
- Attachment buttons save to a user-selected destination and do not launch files.
- Accounts lets you remove a saved sign-in, local drafts and cache. Remote mail is
  preserved.

Mail prefers the HTML alternative when available, preserving sender CSS, tables,
colours, linked stylesheets and inline/CID images. Remote HTTP(S) images load as
part of the message and may notify their hosts that it was opened. A disposable,
ephemeral WebKitGTK renderer returns pixels and link rectangles to the Avalonia
reader; scripts, forms, frames, media, local file access and in-app navigation
are blocked by the document policy. Links are native keyboard-accessible controls
and open in the default browser; mail links open Compose. **More → View plain
text** provides selectable text; **Copy message text** copies the text alternative.
Plain messages remain native selectable text. If rendering is unavailable or a
message exceeds layout limits, its text remains readable. The helper requires
`webkit2gtk-4.1` and XWayland at runtime; `scripts/build-mail-renderer.sh` builds it
with `cc` and `pkg-config`, and the shell project includes it in build/publish.
The application window and reader scrolling remain native Avalonia controls.

Forward quotes the message text and includes the original `.eml` when it has
attachments, preserving their contents. There is no rich
HTML editor, calendar integration, conversation grouping, automatic provider
discovery, push/IDLE subscription, or background service when the app is closed.

## Storage and delivery

Account metadata, draft text, message summaries and downloaded MIME messages live
under `$XDG_DATA_HOME/daisyos/mail` (default `~/.local/share/daisyos/mail`). The directory
is user-only; files are not encrypted at rest. Credentials live in the system keyring.
Summary writes use atomic replacement. MIME cache identity includes account,
folder and UID; UIDVALIDITY changes invalidate downloaded messages. Cached summaries
and previously opened messages remain readable when offline.

SMTP acceptance is the point of successful delivery. A failed Sent-copy operation
must not cause a resend. After an uncertain SMTP failure or timeout, check Sent
before retrying. Gmail and Microsoft file submitted mail themselves; generic IMAP
accounts append to the server's Sent folder when advertised.

## Validation

```sh
dotnet build src/DaisyOS.Mail/DaisyOS.Mail.csproj
dotnet run --project scripts/mail-ui-acceptance
```

The acceptance runner uses isolated temporary account/cache fixtures and a closed
loopback port. It checks MIME recipients and attachments, safe HTML conversion,
mail links, signed identity validation (including tampering, issuer, audience,
nonce and expiry rejection), provider-only onboarding, the shared combo-box template,
fractional-scale text editing, offline reading, draft recovery, search, filters,
selection preservation and compact compose actions. It captures onboarding, inbox,
compose, account setup, account management, drafts and move screens
in both themes at 1000 and 1380 pixels. Screenshots go to `artifacts/mail`.
It does not authenticate to or send mail through a live provider.

## System app UI

Mail uses the shared sidebar navigation styles, SearchBar, DaisyIcon,
AppWindowTitleBar and semantic button variants. Spacing, typography, surfaces,
selection states and dialog actions use the DaisyOS theme tokens. Mail’s scoped
list styles are shared between the main window and its dialogs.

Dialog actions stay fixed while forms scroll. Cc/Bcc expands on demand.
Account sign-in is a list of provider buttons rather than a configuration form. Less frequent message actions live in the More actions menu. Filtering
and refresh preserve the selected message instead of clearing the reader.

Reader changes use a short, interruptible 4 px reveal through GuiMotion. Menus
use the existing MotionBehavior; both respect the system reduced-motion setting.
Editable forms stay on an opaque, untransformed surface. Mail excludes TextBox and
TextPresenter glyph layers from theme brush interpolation while retaining shared
border hover/focus transitions. Typing and caret behavior are checked at 125% and
150% scaling; native Wayland/IME rendering still needs interactive verification.
The sole account dropdown uses the global shared system template and adaptive-width
behavior, capped to its sidebar width. There is no provider dropdown.
The acceptance runner captures 32 themed states, including short compose windows.

## Desktop integration and diagnostics

Mail is part of the built-in application catalog, supports email-related launcher
searches, and is included in the default taskbar order for new users. Existing
users can install and pin it with `scripts/install-mail-app.sh <binary> --pin`.
The installation is independent of browser sign-in configuration.

`DaisyOS.Mail --check-sign-in` reports registration readiness without printing
values or launching a browser. `DaisyOS.Mail --pin-to-taskbar` uses the desktop
settings service and preserves existing taskbar order. Provider configuration can
be shipped with `-p:DaisyMailProviderConfig=<file>`; see the registration guide.

Sign-in failures identify the failing step (authorization, identity verification,
keyring, receiving, sending, or saving). Unexpected failures also write
`last-sign-in-error.txt` in the mail data directory, containing only the timestamp,
step and exception type. No credentials, email addresses or provider responses
are recorded. On Google’s permission screen, grant the requested mail access;
identity-only consent cannot connect a mailbox.

## Background notifications

Mail can check for new messages while closed. Install the per-user timer with
`scripts/install-mail-background.sh` (remove it with `--remove`). It runs
`daisyos-mail --background-check` every 3 minutes, which syncs each inbox
incrementally and sends a desktop notification for new unread mail. `daisyos-mail`
must be on `PATH`, and the keyring must be unlocked.
