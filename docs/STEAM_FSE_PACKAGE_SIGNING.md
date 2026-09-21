# Steam FSE package signing

The release pipeline produces a normal packaged `SteamInputAddonforClaw.FseHome.msix` and signs it before Velopack packaging. The production signing certificate is not stored in this repository and is not generated during a release.

Release automation requires these GitHub Actions secrets:

- `FSE_SIGNING_CERTIFICATE_BASE64` — the base64-encoded PFX for the stable FSE package signing identity.
- `FSE_SIGNING_CERTIFICATE_PASSWORD` — the PFX password.

The PFX subject must exactly match the manifest `Publisher` value (`CN=SteamInputAddonforClaw`). The release job writes the PFX only to the runner temporary directory, passes it to `package-fse-home.ps1`, and removes it in a `finally` block. It never prints the password or stores either value in a release artifact.

The production model is a certificate chain trusted by supported Windows installations. The release pipeline does not use Developer Mode or install a private/root certificate on user machines. CI uses a disposable self-signed certificate only to exercise the packaging and verification contract; that certificate is removed before the job completes and is never published.

The FSE package keeps the stable identity `SteamInputAddonforClaw.FseHome` and application ID `App`. Only the four-part package version changes between Addon releases. The normal installer receives the signed package inside the Velopack payload, and Runtime provisioning uses the package directly without invoking repository PowerShell scripts.
