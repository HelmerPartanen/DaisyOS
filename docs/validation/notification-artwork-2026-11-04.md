# Notification artwork — 2026-11-04

The reported card displayed a generic editor icon in its header and the sender's logo in the large content-image area. Previously the header resolved icon and app identities together, while every `image-path` hint became a preview.

The shared `NotificationArtwork` resolver now separates those roles. It recognizes logo hints through installed icon locations, theme icon names, packaged Linux resources, matching sender names, or an exact match with header artwork. Recognized logo hints render in the header and do not create a preview block. Strong sender identities resolve independently of generic icon aliases; a friendly display name alone does not override an explicit icon file. File URLs and files whose names resemble built-in applications retain their actual image.

The [freedesktop notification specification](https://specifications.freedesktop.org/notification/latest/icons-and-images.html) distinguishes `app_icon` from content-image hints. Logo promotion is compatibility handling for clients that send their application logo through an image hint. Unknown hints remain previews; square aspect ratio or an arbitrary folder named `icons` is insufficient evidence to suppress a genuine attachment.

Both header and preview converters consume the shared resolver. Genuine previews use Uniform stretch to preserve the whole image. Corrupt images are checked before entering the current Avalonia decoder's null-codec path; decoding also bounds the original pixel count and avoids unnecessarily upscaling tiny sources. Failed previews collapse their entire area. The dismiss control uses the shared round icon-button style and aligns with the 32 px header icon.

Validation: Release build/test compilation passed and **14 focused headless UI tests passed**. Cases cover logo hints, local paths and file URLs, explicit icon files, sender identity, friendly-name ambiguity, real square attachments, corrupt/missing images, replacement clearing, compact layout, uncropped previews, card actions/dismissal, lifetime/queue behavior, and native-sized toast layout. Rendered compact and attachment cards were reviewed. Whitespace checks pass.

```sh
dotnet test tests/DaisyOS.ShellDemo.Tests/DaisyOS.ShellDemo.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~NotificationArtworkTests|FullyQualifiedName~NotificationPresentationTests' -m:2
```

The tests exercise the shared presentation path with fixtures, not a live Antigravity notification or native KWin presentation. The running shell was not restarted. Notification payload types, action routing, and lifetime behavior are unchanged.
