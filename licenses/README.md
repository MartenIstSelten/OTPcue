# Release license sources

The release build copies OTPcue's MIT license to `LICENSE.txt` and combines
the full license and NOTICE texts from restored NuGet packages into
`THIRD-PARTY-NOTICES.md`. It includes package versions, authors and copyright
metadata. The Velopack package is checked before release to ensure both files
are included in the installed app payload.

The files below supplement packages that do not contain standalone license
texts. They are original upstream documents, not newly assigned licenses. The
release build embeds their text in `THIRD-PARTY-NOTICES.md`:

- MailKit 4.17.0: https://raw.githubusercontent.com/jstedfast/MailKit/4.17.0/LICENSE
- MimeKit 4.17.0: https://raw.githubusercontent.com/jstedfast/MimeKit/4.17.0/LICENSE
- Velopack 1.2.161: https://raw.githubusercontent.com/velopack/velopack/92d6a1c91716729d449034df5c50307dcce39493/LICENSE
- Microsoft.Windows.SDK.NET.Ref 10.0.19041.57: https://aka.ms/WinSDKLicenseURL

When upgrading a package without an embedded license, add the original license
for the new version here. The release build fails if a license text is missing.
