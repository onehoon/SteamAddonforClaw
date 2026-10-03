# Steam FSE package signing

The fixed FSE MSIX is signed locally when the FseHome payload changes. Normal CI and release workflows consume only the committed signed MSIX and public CER; they do not rebuild or sign the FSE package.

The permanent FSE-only code-signing identity has subject `CN=SteamInputAddonforClaw`. Its exportable private key is kept in the ignored local path:

- `.private/signing/SteamInputAddonforClaw.FseHome.pfx`

Pinned certificate thumbprint: `F1155636D18C99BAAE2F835309A493BF08BCA65B`.

Reuse this same PFX for every future FSE MSIX update. Do not generate a replacement certificate, use another product certificate, commit the PFX, or store its password in the repository. Keep the encrypted PFX outside version control and store its password in a password manager. The matching public CER and signed MSIX are distribution artifacts and may be committed.

For development machines with the pre-release 1.0.0.0 package signed by the lost identity, uninstall that exact package before registering 1.0.1.0. No in-product migration for the old signer is included.

For a local package rebuild, enter the password securely and pass the resulting `SecureString` to the existing packaging script:

```powershell
$fsePassword = Read-Host -AsSecureString
.\scripts\package-fse-home.ps1 `
    -PublishDirectory <FseHome-publish-directory> `
    -CertificatePath .private\signing\SteamInputAddonforClaw.FseHome.pfx `
    -CertificatePassword $fsePassword `
    -FseVersion 1.0.1.0
```

Never add PFX/password GitHub Actions secrets or restore release-time FSE signing. The FSE package keeps the stable identity `SteamInputAddonforClaw.FseHome` and application ID `App`; its separately versioned fixed artifact is bundled unchanged by normal Addon CI/release.
