# OTPcue - Autofill OTP codes on Windows

macOS autofills your verification codes from Mail. Windows leaves you copying and pasting. **OTPcue brings that convenience to Windows.**

Connect your inbox, receive a code, and click the suggestion to fill it in—right in your app or browser. Codes are processed locally, with no browser extension required.

## Features

- **Local processing:** Emails are processed on your computer, with no telemetry or email content logging.
- **Encrypted credentials:** Passwords and OAuth tokens are protected with Windows DPAPI and stored for your Windows user account.
- **Email connections:** Connect through IMAP using a password or app password, or use Google OAuth for Gmail. Multiple accounts are supported.
- **Click to fill:** New codes appear as suggestions next to supported input fields in browsers and Windows apps.
- **Configurable suggestions:** Adjust the countdown, use codes once, and optionally restrict suggestions to detected verification code fields.
- **Demo mode:** Try autofill without connecting an email account.

## Installation

For Windows 10 and 11 (x64).

1. Download `OTPcue-win-x64.exe` from [Releases](https://github.com/MartenIstSelten/OTPcue/releases) once a release is available.
2. Run the installer and start OTPcue. Required runtimes are included.
3. Add an account, verify the connection, and save it. Request a new verification email, then click the suggestion next to the input field.

To try the app, start demo mode and click a text field in another app. Click the suggestion to insert the demo code.

## Notes

The app processes new inbox messages without modifying them. It detects numeric codes containing 4–8 digits. Some input fields are unsupported, including codes split across multiple individual boxes.

Gmail and iCloud can be connected using app passwords.

Google OAuth is currently in testing mode; Google verification is pending. Sign-in is available only to explicitly approved Google test accounts. In testing mode, authorization expires after seven days, requiring another Google sign-in in OTPcue. Alternatively, connect Gmail with an app password if your Google account supports it.

## Development and contributions

See [CONTRIBUTING.md](CONTRIBUTING.md) for prerequisites, build instructions, tests, and setting up your own Google OAuth client.

Please report security issues according to [SECURITY.md](SECURITY.md).

## License

[MIT License](LICENSE)
