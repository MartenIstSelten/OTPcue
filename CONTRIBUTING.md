# Contributing to OTPinsert

Bug reports and focused pull requests are welcome. Please open an issue to discuss larger changes before implementing them. For security issues, follow [SECURITY.md](SECURITY.md).

## Prerequisites

- Windows 10 version 2004 (build 19041) or later, or Windows 11, x64.
- .NET SDK 10.0.401 or a newer patch in the same SDK feature band, as specified in `global.json`.
- Git and PowerShell. PowerShell 7 is recommended for the scripts and release builds.

## Build and run

From the repository directory:

```powershell
dotnet restore OTPinsert.slnx
dotnet build OTPinsert.slnx -c Debug
dotnet run --project src/OTPinsert.WinUI/OTPinsert.WinUI.csproj -c Debug
```

Demo mode does not require an email account. Use synthetic messages and example addresses such as `user@example.test` for testing.

## Verification

```powershell
./scripts/Verify-Code.ps1
```

The script builds with warnings treated as errors, runs the xUnit tests through `dotnet test` for Core, Google, WinUI, Updates, and IMAP message processing, and checks whitespace formatting. It writes build output to `artifacts/verification/`. GitHub Actions runs the same checks through `verify.yml` and before publishing a release. TRX test reports are written to `artifacts/verification/test-results/`.

Whitespace formatting is checked in CI. To apply the existing formatting conventions locally, run `dotnet format whitespace OTPinsert.slnx --no-restore`.

To include interactive Windows tests in a signed-in desktop session:

```powershell
./scripts/Verify-Code.ps1 -Interactive
```

Open `tests/manual/otp-field.html` in a browser to test autofill manually.

## Code structure

- `OTPinsert.Core`: OTP detection and lifecycle, without Windows dependencies.
- `OTPinsert.Platform`: mail providers, encrypted storage, localization, updates, and Windows integration. This library is shared by the application and tests; tests do not compile copies of production files.
- `OTPinsert.WinUI`: window composition, dialogs, and presentation. Settings and account actions live in focused partial files. `AccountFormErrors` handles field error presentation; `AccountVerification` in Platform owns connection verification and cancellation independently of the controls.

`AccountValidation` is shared by account drafts and persisted accounts. Keep connection invariants there so loading or saving accounts cannot bypass form validation. `AutofillController` owns focus tracking, popup visibility, insertion state, and its resources; `MainWindow` renders the current code and coordinates application actions.

`OtpStore.CodeAdded` signals accepted new codes independently of UI refreshes, consumption, and retries. `MainWindow` checks the saved sound preference and dispatches playback to the UI thread; `CodeNotificationSound` owns the embedded WAV and playback resources. Sound is enabled by default, including for older settings files. The volume slider is visible while sound is enabled, and its saved 0–100 percent value defaults to 35 for new and older settings without a saved volume. Playback scales the bundled PCM samples without changing Windows volume; 0 skips playback. Recreate the "Tiefer Puls" chime (variant 1: 0.42 seconds, notes 70 ms apart) with `./scripts/New-CodeNotificationSound.ps1`.

`AccountFolderSelection` owns folder choices, selection, and cancellable provider operations. Credential and transport edits preserve the selection; changing the provider, server, or username invalidates the previous mailbox's choices. Canceled or failed requests must not replace the selection. Both connection verification and monitoring open the selected IMAP folder read only through `MailFolders.OpenImapFolderAsync`.

Google token caching retains only DPAPI ciphertext. Each request still decrypts its own snapshot, checks the account/client binding, and lets the Google SDK handle expiry and refresh. Unchanged snapshots are not rewritten. File metadata changes invalidate cached bytes; deleting credentials also invalidates in-flight authorization so it cannot restore the deleted cache. Plaintext serialization buffers are cleared after use.

For account edits, enabling/disabling, and removal, `AccountChange` treats a successful save as the commit point. A failed save leaves monitoring untouched. After commit, the old worker is awaited before the UI adopts the saved state; worker and credential-cleanup failures are diagnosed and shown as cleanup warnings. The stop callback must guarantee worker termination even when it reports an error, as `AccountMonitor.StopAsync` does.

Platform types use the `OTPinsert.Platform` namespace, including Windows integration. Keep private fields in lower camel case in Core and Platform and use the existing underscore prefix in window classes.

`FocusService` tries UI Automation first and uses `MsaaField.ReadTarget` only when UIA supplies no eligible input. `MsaaNative` isolates Windows/COM interop and the fixed IAccessible2 ABI; `MsaaField` owns focus detection, field metadata, and hit testing. `FocusTarget` belongs to exactly one provider. MSAA identity uses the IAccessible2 window handle and node ID when available, with COM identity as the fallback. Keep provider calls on the focus worker, bound all tree walks, and revalidate field identity and foreground before insertion. Protected, read-only, disabled, invisible, and offscreen MSAA fields are excluded.

Use stable `TextId` values and the German/English `.resx` resources for UI text. Use `LocalizedText` for retained status messages so language changes render the original arguments without translating account data. Keep complete sentences with format placeholders in the resources.

Google, update, and mail processing scenarios are separate xUnit cases, so they can be selected with `dotnet test OTPinsert.slnx -c Release --filter FullyQualifiedName~MailProcessingTests` (replace the class name as needed). The small CLI runners reuse these same cases for manual runs.

Run all offline tests directly with `dotnet test OTPinsert.slnx -c Release`. Desktop automation remains opt-in through `Verify-Code.ps1 -Interactive` and needs a signed-in Windows session.

## Local diagnostics

Unexpected failures in focus tracking, click callbacks, account changes, connection verification, mail monitoring, updates, and settings emit opt-in events from the `OTPinsert-Diagnostics` EventSource. Attach an EventSource listener or tracing tool and enable warning events to inspect them. The app creates no diagnostic file and sends no telemetry; these events have no output unless a listener enables the source.

Each failure contains only a fixed operation name, the exception type name, and the numeric `HResult`. Never add exception messages, stack traces, inner exceptions, account identifiers, field text, mail contents, OTPs, passwords, or tokens to these events. Provider exception details can contain sensitive data. The diagnostics test checks the emitted payload against an exception containing synthetic secrets.

MSAA regression cases live in `MsaaFocusTests` and run through the normal test project.

## Your own Google OAuth client

Use your own Google Cloud OAuth client for forks and personal testing:

1. Create a Google Cloud project and enable the Gmail API.
2. Configure the OAuth consent screen and add your test account while the project is in testing mode.
3. Create an OAuth client with the **Desktop app** application type and download its client configuration. Do not use a web server client.
4. In OTPinsert, select the Gmail provider that uses Google sign-in. Enable the option for your own Google app configuration and enter the matching client ID and desktop `client_secret`.
5. Enter your primary Gmail address, sign in, and save the verified account.

Alternatively, set the defaults for newly configured accounts in your current PowerShell session before starting the app:

```powershell
$env:OTP_GOOGLE_CLIENT_ID = '<your-desktop-client-id>.apps.googleusercontent.com'
$env:OTP_GOOGLE_CLIENT_SECRET = '<matching-desktop-client-secret>'
dotnet run --project src/OTPinsert.WinUI/OTPinsert.WinUI.csproj -c Debug
```

The app does not load `.env` files automatically. Existing saved accounts retain their own configuration.

The shared desktop configuration in the source code identifies the official app. An installed desktop app cannot keep these values confidential; they are not user access or refresh tokens. Sign-in uses PKCE and the `gmail.readonly` scope. This scope allows reading the mailbox, including messages other than verification emails. The app processes new messages locally. Publishing the repository does not replace Google OAuth verification.

See [Google's desktop OAuth documentation](https://developers.google.com/identity/protocols/oauth2/native-app) and [Gmail API scopes](https://developers.google.com/workspace/gmail/api/auth/scopes).

## Submitting changes

- Keep changes focused on one topic and follow the existing project and formatting conventions.
- Add appropriate coverage to the existing tests when behavior changes, and run `Verify-Code.ps1`.
- Describe the problem, your changes, and the checks performed in the pull request.
- Do not commit real emails, OTPs, app passwords, user tokens, or downloaded client JSON files, or publish them in logs or screenshots.
- If needed, configure your GitHub no-reply address for future commits with `git config --local user.email '<your-GitHub-no-reply-address>'`. This does not change existing commits.

## Building a release

```powershell
./scripts/Build-Release.ps1 -Version 0.1.0
```

Use a stable version number that has not been built before. The installer and update packages are written to `artifacts/releases/<Version>/packages/`. For forks, also pass `-UpdateRepositoryUrl https://github.com/<owner>/<repo>`. Public release repositories support anonymous updates. Private repositories require a local token on each test PC.

The GitHub release workflow publishes after successful checks when a `v*` tag is pushed or the workflow is started manually. It requires write access to releases; the regular verification workflow has read access only.

### Testing updates with a private repository

Create an expiring fine-grained GitHub personal access token restricted to your release repository with **Contents: Read-only**. On the test PC, under the same Windows user that runs OTPinsert:

```powershell
./scripts/Set-LocalUpdateToken.ps1
# For a fork, add -RepositoryUrl https://github.com/<owner>/<repo>
```

Enter the token only at the hidden prompt; do not paste it into chat, a command, source code, a GitHub secret, or a build setting. The script stores only a DPAPI-encrypted credential under `%LOCALAPPDATA%\OTPinsert\update-auth\<repository-hash>.update-token.dat`, outside the checkout and release output. The app reads it at startup and uses it only for the matching repository. Restart OTPinsert after setting or changing it. Invalid or unavailable credentials fall back to anonymous access; private update checks will then fail normally.

The token is never a build input or installer resource. `*.update-token.dat` is also ignored by Git as a precaution. The release workflow uses its own temporary GitHub Actions token, independently of your test PC credential. Processes running as your Windows user can decrypt DPAPI data, so treat this local credential as a secret.

Install an initial release, publish a higher stable version, then check, download, and restart through the app. To remove the local credential after making the repository public:

```powershell
./scripts/Set-LocalUpdateToken.ps1 -Remove
```

Restart OTPinsert and revoke the test token in GitHub. For forks, pass the same `-RepositoryUrl` when removing it.
