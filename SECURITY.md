# Reporting security issues

Please do not publish vulnerability details, real emails, OTPs, passwords, or OAuth tokens in public issues, pull requests, or comments.

Use [GitHub Private Vulnerability Reporting](https://github.com/MartenIstSelten/OTPinsert/security/advisories/new) for confidential reports when “Report a vulnerability” is available in the Security section. If this reporting channel has not been enabled yet, open an issue requesting a private contact channel, without technical details or private data. Share details only through the private channel provided by the maintainer.

A private report should include the affected version or commit, prerequisites, reproduction steps using synthetic data, and the expected impact.

## Supported versions

The project is under development. Security fixes are provided for the current default branch and the latest stable release. Older versions do not have a separate maintenance commitment.

## Relevant areas

Areas of particular interest include protection of IMAP credentials and OAuth tokens, unintended disclosure of email content or OTPs, insertion into the wrong input field, and update integrity.

The shared Google desktop client configuration is included in the installed app and cannot be treated as a confidential server secret. User access tokens, refresh tokens, and app passwords are separate credentials and must remain protected.
