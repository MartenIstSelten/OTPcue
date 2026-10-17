# OTPinsert — Fill verification codes on Windows

OTPinsert brings email verification codes to the app or browser where you need them. Connect your inbox, request a code, and click the suggestion next to the input field to fill it in.

Your emails are processed on your computer, and you don't need a browser extension.

## Features

- **Fill codes with a click.** Click a suggestion next to a supported input field in your browser or Windows app to insert the code.
- **Choose what to monitor.** Select the IMAP folder or Gmail label where your verification emails arrive.
- **Copy codes automatically.** Turn on automatic copying if you prefer to paste codes yourself.
- **Keep processing local.** OTPinsert processes emails on your computer, with no telemetry or email content logging.
- **Protect your credentials.** Passwords and OAuth tokens are encrypted with Windows DPAPI for your Windows user account.
- **Connect multiple accounts.** Use IMAP with a password or app password, or connect Gmail through Google OAuth. See the Gmail sign-in notes below for current restrictions.
- Demo mode lets you test autofill without connecting an email account.

## Installation

OTPinsert runs on Windows 10 and 11 (x64).

1. Download `OTPinsert.exe` from [Releases](https://github.com/MartenIstSelten/OTPinsert/releases).
2. Run the installer and open OTPinsert. The required runtimes are included.
3. Add your email account, verify the connection, and save it.
4. Request a new verification email, click the code field in your app or browser, and click the OTPinsert suggestion to fill it in.

Want to try it before connecting your inbox? Start demo mode, click a text field in another app, then click the suggestion to insert a sample code.

## Things to know

OTPinsert watches for new messages in your selected folder and leaves them unchanged. It ignores messages that are already there when monitoring starts or when you switch folders.

It detects numeric codes containing 4–8 digits.

### Gmail and iCloud sign-in

Gmail and iCloud can be connected using app passwords.

Google OAuth is currently in testing mode while Google verification is pending. Only approved Google test accounts can sign in this way. Authorization expires after seven days in testing mode, so you'll need to sign in again in OTPinsert. You can also connect Gmail with an app password if your Google account has 2FA enabled.

## Development and contributions

See [CONTRIBUTING.md](CONTRIBUTING.md) for prerequisites, build instructions, tests, and setting up your own Google OAuth client.

Please report security issues according to [SECURITY.md](SECURITY.md).

## License

[MIT License](LICENSE)
