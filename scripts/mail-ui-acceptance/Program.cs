using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.Mail;
using DaisyOS.Shell.Controls;
using MimeKit;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/mail");
Directory.CreateDirectory(output);
var root = Path.Combine(Path.GetTempPath(), "daisy-mail-qa-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("XDG_DATA_HOME", root);
AppBuilder.Configure<MailQaApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().UseHarfBuzz()
 .With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/Inter#Inter" }).SetupWithoutStarting();
try
{
    // Never launch a live provider login from the isolated acceptance runner.
    var isolatedRegistration = Path.Combine(root, "mail-providers.json");
    File.WriteAllText(isolatedRegistration, "{}");
    Environment.SetEnvironmentVariable("DAISYOS_MAIL_PROVIDER_CONFIG", isolatedRegistration);
    Environment.SetEnvironmentVariable("DAISYOS_MAIL_GOOGLE_CLIENT_ID", null);
    Environment.SetEnvironmentVariable("DAISYOS_MAIL_MICROSOFT_CLIENT_ID", null);
    MailIdentityChecks.Run();
    var registrationFixture = Path.Combine(root, "registration-fixture.json");
    File.WriteAllText(registrationFixture, "{\"GoogleClientId\":\"123-test.apps.googleusercontent.com\",\"MicrosoftClientId\":\"11111111-1111-1111-1111-111111111111\"}");
    var provisioned = MailProviderRegistration.Load(registrationFixture, useEnvironment: false);
    Check(provisioned.GoogleConfigured && provisioned.MicrosoftConfigured, "Provider registration loads from a provisioned file without terminal environment variables");
    Check(!MailProviderRegistration.Load(isolatedRegistration, useEnvironment: false).GoogleConfigured, "Missing registration cannot masquerade as working sign-in");
    var link = MailText.ParseMailto("mailto:you@example.org?subject=Hello%20world&body=First%0ASecond&cc=copy%40example.org");
    Check(link.To == "you@example.org" && link.Subject == "Hello world" && link.Body.Contains('\n') && link.Cc == "copy@example.org", "Mail links decoded into editable drafts");
    var storage = new MailStorage();
    var account = new MailAccount { Email = "me@example.edu", Name = "Test User", ImapHost = "127.0.0.1", ImapPort = 1, SmtpHost = "127.0.0.1" };
    Check(!MailStorage.CacheKey(account.Id, "a/b").Contains('/'), "Folder names cannot escape cache root");
    Check(MailStorage.CacheKey(account.Id, "Inbox") != MailStorage.CacheKey(account.Id, "inbox"), "Case sensitive mailbox identity");
    var html = MailText.FromHtml("<html><head><style>secret-style</style></head><body><p>Hello &amp; welcome</p><script>tracking()</script><img src='https://tracker.invalid/x'><p>Next line</p></body></html>");
    Check(html.Contains("Hello & welcome") && html.Contains("Next line") && !html.Contains("tracking") && !html.Contains("secret-style") && !html.Contains("tracker.invalid"), "HTML converted to safe text");
    var attachmentPath = Path.Combine(root, "résumé.txt"); File.WriteAllText(attachmentPath, "attachment bytes ✓");
    var draft = new MailDraft { To = "Recipient <you@example.org>", Cc = "copy@example.org", Bcc = "hidden@example.org", Subject = "Unicode ✓", Body = "Hello\nworld", InReplyTo = "original@example.org", Attachments = [attachmentPath] };
    var mime = MailService.BuildMessage(account, draft);
    using var wire = new MemoryStream(); mime.WriteTo(wire); wire.Position = 0; var restored = MimeMessage.Load(wire);
    Check(restored.Subject == draft.Subject && restored.To.Mailboxes.Single().Address == "you@example.org" && restored.Cc.Count == 1 && restored.Bcc.Count == 1, "Recipients and Unicode MIME roundtrip");
    Check(restored.InReplyTo == draft.InReplyTo && restored.TextBody!.Contains("world") && restored.Attachments.Count() == 1, "Thread headers, text and attachment preserved");
    using var decoded = new MemoryStream(); ((MimePart)restored.Attachments.Single()).Content.DecodeTo(decoded);
    Check(global::System.Text.Encoding.UTF8.GetString(decoded.ToArray()) == "attachment bytes ✓", "Attachment bytes preserved");
    var bad = false; try { MailService.BuildMessage(account, new MailDraft { To = "invalid address <>" }); } catch (ParseException) { bad = true; }
    Check(bad, "Malformed recipients rejected before sending");
    foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
    foreach (var width in new[] { 1000, 1380 })
    {
        File.Delete(Path.Combine(storage.Root, "accounts.json"));
        var window = new MailWindow { Width = width, Height = 820, RequestedThemeVariant = theme }; window.Show(); Tick(10);
        Check(!window.FindControl<Grid>("MailWorkspace")!.IsVisible && window.FindControl<MailAccountSetup>("AccountSetup")!.IsVisible, "Only account setup is shown without an account");
        Check(!window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible), "Setup has no technical configuration fields");
        Check(window.FindControl<TextBlock>("Subject")!.Text == "", "No empty reader instructions");
        Check(!window.FindControl<Button>("RefreshButton")!.IsEnabled && !window.FindControl<Button>("DraftsNavigation")!.IsEnabled, "Account-dependent actions are unavailable before setup");
        Save(window, $"onboarding-{theme.Key}-{width}");
        var firstMailto = window.OpenMailtoAsync("mailto:first@example.org?subject=First%20message"); Tick(4); firstMailto.GetAwaiter().GetResult();
        Check(window.OwnedWindows.Count == 0 && !window.FindControl<Grid>("MailWorkspace")!.IsVisible, "Mail links retain setup as the only first-run page");
        var connected = window.FindControl<MailAccountSetup>("AccountSetup")!.Connected!(account); Tick(20); connected.GetAwaiter().GetResult();
        Check(window.FindControl<Grid>("MailWorkspace")!.IsVisible && !window.FindControl<MailAccountSetup>("AccountSetup")!.IsVisible, "A saved connection replaces onboarding with the mailbox");
        var pending = window.OwnedWindows.Single();
        Check(pending.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ToField").Text == "first@example.org", "Mailto recipient survives account setup");
        pending.Close(); window.Close(); Tick(3);
        File.WriteAllText(Path.Combine(storage.Root, "accounts.json"), JsonSerializer.Serialize(new[] { account }));
        File.WriteAllText(Path.Combine(storage.Root, "folders-" + account.Id + ".json"), JsonSerializer.Serialize(new[] { new MailFolder("Inbox", "INBOX"), new MailFolder("Archive", "Archive") }));
        var key = MailStorage.CacheKey(account.Id, "INBOX");
        var items = new[] {
          new MailItem { Uid = 1, Subject = "Desktop compositor specifications", Sender = "Alex Rivera", Address = "alex@example.org", Date = DateTimeOffset.Now, Unread = false, Starred = true, Preview = "Attached are the final surface tokens for the compositor." },
          new MailItem { Uid = 2, Subject = "Security advisory", Sender = "Elena Novak", Address = "elena@example.org", Date = DateTimeOffset.Now, Unread = true, Preview = "The automated testing cluster flagged an unexpected issue." }
        };
        File.WriteAllText(Path.Combine(storage.Root, key + ".json"), JsonSerializer.Serialize(items));
        var incoming = new MimeMessage(); incoming.From.Add(new MailboxAddress("Alex Rivera", "alex@example.org")); incoming.To.Add(MailboxAddress.Parse(account.Email)); incoming.Subject = items[0].Subject; incoming.Date = DateTimeOffset.Now;
        incoming.Body = new TextPart("plain") { Text = "Hey team,\n\nFollowing up on yesterday's graphics architecture sync. Attached are the final surface tokens.\n\nPlease review the compositor changes before Friday.\n\nThanks,\nAlex" };
        incoming.WriteTo(Path.Combine(storage.Root, key + "-1.eml"));
        window = new MailWindow { Width = width, Height = 820, RequestedThemeVariant = theme }; window.Show(); Tick(30);
        Check(window.FindControl<Grid>("MailWorkspace")!.IsVisible && !window.FindControl<MailAccountSetup>("AccountSetup")!.IsVisible, "Connected accounts open the mailbox workspace");
        var selector = window.FindControl<ComboBox>("Accounts")!;
        Check(ComboBoxBehavior.GetUseAdaptiveWidth(selector) && selector.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "Background"), "Account selector uses the shared system combo-box template");
        var messages = window.FindControl<ListBox>("Messages")!;
        Check(messages.ItemCount == 2, "Cached mailbox displayed after connection failure");
        window.FindControl<SearchBar>("Search")!.Text = "Elena"; Tick(3); Check(messages.ItemCount == 1, "Sender search");
        window.FindControl<SearchBar>("Search")!.FindControl<Button>("ClearButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Tick(3);
        Check(messages.ItemCount == 2 && window.FindControl<SearchBar>("Search")!.Text == "", "Shared search clear restores the mailbox");
        window.FindControl<CheckBox>("StarredOnly")!.IsChecked = true; Tick(3); Check(messages.ItemCount == 1, "Star filter");
        window.FindControl<CheckBox>("StarredOnly")!.IsChecked = false; Tick(3);
        messages.SelectedIndex = 0; Tick(30);
        Check(window.FindControl<SelectableTextBlock>("Body")!.Text!.Contains("Hey team"), "Cached body readable offline");
        Check(window.FindControl<WrapPanel>("MessageActions")!.IsEnabled, "Message actions enabled after loading");
        var readerText = window.FindControl<SelectableTextBlock>("Body")!.Text;
        window.FindControl<CheckBox>("StarredOnly")!.IsChecked = true; Tick(3);
        Check((messages.SelectedItem as MailItem)?.Uid == 1 && window.FindControl<SelectableTextBlock>("Body")!.Text == readerText, "Filtering retains the reader and selection without flicker");
        window.FindControl<CheckBox>("StarredOnly")!.IsChecked = false; Tick(3);
        Save(window, $"inbox-{theme.Key}-{width}");
        window.FindControl<Button>("ComposeButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Tick(4);
        var compose = window.OwnedWindows.Single();
        Check(compose.Background is SolidColorBrush && compose.ActualThemeVariant == theme, "Compose has a themed opaque surface");
        compose.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ToField").Text = "recipient@example.org";
        compose.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "SubjectField").Text = "Recover this draft";
        compose.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ComposeBody").Text = "Draft text survives closing.";
        Save(compose, $"compose-{theme.Key}-{width}");
        window.RequestedThemeVariant = theme == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark; Tick(35);
        Check(compose.ActualThemeVariant == window.ActualThemeVariant, "Open compose follows live owner theme changes");
        window.RequestedThemeVariant = theme; Tick(35);
        var sendAction = compose.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string label && label == "Send");
        var sendOrigin = sendAction.TranslatePoint(default, compose)!.Value;
        Check(sendOrigin.Y + sendAction.Bounds.Height <= compose.Bounds.Height && sendOrigin.Y >= 0, "Compose actions stay visible without scrolling");
        var editor = compose.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "ComposeBody");
        Check(!DaisyOS.Shell.Services.Theming.ThemeTransitionPolicy.GetEnabled(editor), "Editable text does not interpolate glyph colors");
        Check(editor.GetVisualAncestors().OfType<Control>().All(c => c.RenderTransform is null && c.Opacity == 1), "Text inputs have no moving or fading ancestors");
        foreach (var scaling in new[] { 1.25, 1.5 })
        {
            compose.SetRenderScaling(scaling); Tick(4);
            editor.Focus(); editor.SelectAll(); compose.KeyTextInput("Unicode café 日本語\nSecond line"); Tick(4);
            Check(editor.Text == "Unicode café 日本語\nSecond line" && editor.CaretIndex == editor.Text.Length, "Typing and caret stay correct at fractional scaling");
        }
        editor.Text = "Draft text survives closing."; compose.SetRenderScaling(1); Tick(4);
        compose.Height = 480; Tick(5);
        Save(compose, $"compose-compact-{theme.Key}-{width}");
        sendOrigin = sendAction.TranslatePoint(default, compose)!.Value;
        Check(sendOrigin.Y + sendAction.Bounds.Height <= compose.Bounds.Height, "Compose footer remains visible in a short window");
        compose.Close(); Tick(4);
        var savedDrafts = JsonSerializer.Deserialize<List<MailDraft>>(File.ReadAllText(Path.Combine(storage.Root, "drafts.json")))!;
        Check(savedDrafts.Any(d => d.AccountId == account.Id && d.Subject == "Recover this draft" && d.Body.Contains("survives")), "Compose saves recoverable draft on close");
        window.FindControl<Button>("AddAccountButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Tick(4);
        var connect = window.OwnedWindows.Single();
        Check(connect.Background is SolidColorBrush && connect.ActualThemeVariant == theme, "Account dialog inherits owner theme");
        Check(!connect.GetVisualDescendants().OfType<TextBox>().Any(), "Provider chooser never exposes email, password, server or OAuth fields");
        Check(!connect.GetVisualDescendants().OfType<ComboBox>().Any(), "Provider selection is a direct list of sign-in choices");
        connect.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "GoogleSignIn").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Tick(4);
        var signInMessage = connect.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "SignInStatus").Text!;
        Check(!signInMessage.Contains("OAuth") && !signInMessage.Contains("client", StringComparison.OrdinalIgnoreCase), "Unavailable sign-in uses nontechnical copy without pretending to connect");
        Save(connect, $"connect-{theme.Key}-{width}"); connect.Close(); Tick(4);
        ClickNavigation(window, "Manage accounts"); Tick(5);
        var manage = window.OwnedWindows.Single();
        var removeAccount = manage.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string t && t == "Remove account");
        Check(!removeAccount.IsEnabled, "Account removal requires a selection");
        manage.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0; Tick(3);
        Check(removeAccount.IsEnabled, "Account action follows selection");
        Save(manage, $"accounts-{theme.Key}-{width}"); manage.Close(); Tick(4);
        ClickNavigation(window, "Local drafts"); Tick(5);
        var draftsDialog = window.OwnedWindows.Single();
        var continueWriting = draftsDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string t && t == "Continue writing");
        Check(!continueWriting.IsEnabled, "Draft actions require a selection");
        draftsDialog.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0; Tick(3);
        Check(continueWriting.IsEnabled, "Draft selection enables continuation");
        Save(draftsDialog, $"drafts-{theme.Key}-{width}"); draftsDialog.Close(); Tick(4);
        var more = window.FindControl<WrapPanel>("MessageActions")!.Children.OfType<Button>().Single(b => b.Flyout is MenuFlyout);
        ((MenuFlyout)more.Flyout!).Items.OfType<MenuItem>().Single(i => i.Header is string h && h == "Move to folder…").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); Tick(5);
        var moveDialog = window.OwnedWindows.Single();
        var moveAction = moveDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string t && t == "Move");
        Check(!moveAction.IsEnabled, "Move requires a destination");
        moveDialog.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0; Tick(3); Check(moveAction.IsEnabled, "Move action follows destination selection");
        Save(moveDialog, $"move-{theme.Key}-{width}"); moveDialog.Close(); Tick(4);
        var folderList = window.FindControl<ListBox>("Folders")!;
        folderList.SelectedIndex = 1; Tick(20);
        Check(messages.SelectedItem is null && !window.FindControl<WrapPanel>("MessageActions")!.IsEnabled, "Switching folders clears the previous reader and actions");
        folderList.SelectedIndex = 0; Tick(20);
        Check((messages.SelectedItem as MailItem)?.Uid == 1 && messages.ItemCount == 2, "Returning to a cached folder opens its first message");
        window.Close(); Tick(5);
    }
    Console.WriteLine("PASS: signed identity validation, provider-only onboarding, shared dropdowns, fractional-scale editing, MIME, offline reader, draft recovery and 32 themed UI captures.");
}
finally { Directory.Delete(root, true); }
static void ClickNavigation(Window window, string label)
{
    var button = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label).GetVisualAncestors().OfType<Button>().First();
    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
void Save(Window window, string name) { Tick(35); using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame"); bitmap.Save(Path.Combine(output, name + ".png")); }
static void Check(bool value, string label) { if (!value) throw new Exception(label); }
static void Tick(int count) { for (var i = 0; i < count; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); } }
public sealed class MailQaApp : Application
{
 public override void Initialize()
 {
  var shell = new App(); shell.Initialize(); var resources = shell.Resources; shell.Resources = new ResourceDictionary(); Resources = resources;
  foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
 }
}
