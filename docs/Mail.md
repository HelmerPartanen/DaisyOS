# DaisyOS Mail

Mail is an independent Avalonia system application using the shared DaisyOS theme,
window chrome, launcher and taskbar. Run:

```sh
dotnet run --project src/DaisyOS.Mail
```

## Connecting accounts

Install `libsecret` (provides `secret-tool`) and run an unlocked Secret Service keyring
such as GNOME Keyring or KWallet's Secret Service. Passwords and refresh tokens are
stored there; there is no plaintext credential fallback.

Google and Microsoft sign-in uses your system browser with PKCE, a random state,
and a temporary loopback callback. Mail does not ship another application's OAuth
identity. Register your own application before using browser sign-in:

- Google: create a **Desktop app** OAuth client in Google Cloud, configure the
  consent screen, enable Gmail access, and allow your test accounts. Set
  `DAISYOS_MAIL_GOOGLE_CLIENT_ID`. If the desktop client requires its issued secret,
  also set `DAISYOS_MAIL_GOOGLE_CLIENT_SECRET` in the process environment. The
  requested `https://mail.google.com/` scope is restricted; distributing to external
  users requires Google's verification process.
- Microsoft: register a public desktop application in Entra, support the intended
  organization/personal account types, allow a loopback redirect, and grant
  delegated `IMAP.AccessAsUser.All`, `SMTP.Send`, and `offline_access`. Set
  `DAISYOS_MAIL_MICROSOFT_CLIENT_ID`. Tenant policy must permit IMAP and SMTP AUTH;
  administrator consent may be required.
- Alternatively enter the client ID in Add account. Client IDs are public metadata.
- Other IMAP: provide your address, incoming TLS server/port (normally 993), outgoing
  SMTP server/port (587 with required STARTTLS, or 465 with TLS), and a provider
  password or app password. This includes iCloud, Yahoo and many hosted domains.
  Incoming STARTTLS on port 143 is not currently supported.
- `.edu` is a domain suffix, not a mail protocol: choose Google or Microsoft if your
  school uses them, or ask IT for IMAP/SMTP settings. Accounts whose administrators
  prohibit these protocols cannot connect with this client.

Both incoming and outgoing authentication are tested before an account is saved.
Provider verification, consent and live mailbox access cannot be verified without
registered OAuth clients and user test accounts.

Provider references:
[Google IMAP/SMTP](https://developers.google.com/workspace/gmail/imap/imap-smtp),
[Google XOAUTH2](https://developers.google.com/workspace/gmail/imap/xoauth2-protocol),
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
- Compose supports To/Cc/Bcc, plain text, file attachments and attachment removal.
  Drafts save locally every three seconds and when closing. Drafts on this device
  lets you resume or delete them. Attachment references must remain accessible.
- Attachment buttons save to a user-selected destination and do not launch files.
- Accounts lets you remove a saved sign-in, local drafts and cache. Remote mail is
  preserved.

Mail displays plain text, converting HTML-only messages to text. Remote images,
HTML scripting and tracking requests do not run. Forward quotes the message text and includes the original `.eml` when it has
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
mail links, offline reading, draft recovery, invalid account validation, search, filters, selection preservation and compact compose actions, and captures welcome, inbox, compose, account setup, account management, drafts and move screens
in both themes at 1000 and 1380 pixels. Screenshots go to `artifacts/mail`.
It does not authenticate to or send mail through a live provider.

## System app UI

Mail uses the shared sidebar navigation styles, SearchBar, DaisyIcon,
AppWindowTitleBar and semantic button variants. Spacing, typography, surfaces,
selection states and dialog actions use the DaisyOS theme tokens. Mail’s scoped
list styles are shared between the main window and its dialogs.

Dialog actions stay fixed while forms scroll. Cc/Bcc and server settings expand
on demand. Less frequent message actions live in the More actions menu. Filtering
and refresh preserve the selected message instead of clearing the reader.

Reader changes use a short, interruptible 4 px reveal through GuiMotion. Dialogs
and menus use the existing MotionBehavior; all respect the system reduced-motion
setting. Hover, press, focus and theme changes use the existing control transitions.
The acceptance runner captures 32 themed states, including short compose windows.
