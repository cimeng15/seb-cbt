# Safe Exam Browser — CBT Edition

Fork of [Safe Exam Browser for Windows](https://github.com/SafeExamBrowser/seb-win-refactoring) that
integrates with the CBT panel at **https://cbt.smkdata.sch.id**.

The goal: the exam administrator manages the **quit/unlock password** centrally from the CBT panel,
and every running exam client picks it up automatically — no need to regenerate and redistribute a
`.seb` configuration file whenever the password changes.

The CBT URL and endpoint are **compiled into the application**, so it works out of the box without
any `.seb` file at all. A configuration file is still fully supported and overrides the built-in
defaults when supplied.

---

## Built-in defaults (no `.seb` file required)

`SafeExamBrowser.Settings/CbtDefaults.cs` holds the compiled-in values:

| Constant       | Value                                              |
|----------------|----------------------------------------------------|
| `BaseUrl`      | `https://cbt.smkdata.sch.id`                       |
| `KioskUrl`     | `https://cbt.smkdata.sch.id/api/kiosk/settings`    |
| `KioskTimeout` | `5000` ms                                          |
| `KioskAttempts`| `3`                                                |
| `KioskAttemptInterval` | `1000` ms                                  |

These are applied in `DataValues.LoadDefaultSettings()`, which is used whenever the application
starts without a configuration file. As a result, launching `SafeExamBrowser.exe` directly:

- opens `https://cbt.smkdata.sch.id`,
- locks the client down (fullscreen, Alt+Tab / Alt+F4 / Win / F12 / right-click / clipboard / print /
  downloads blocked, navigation restricted to the CBT host),
- and validates the quit/unlock password against the CBT endpoint.

To change the URL, edit `CbtDefaults.cs` and rebuild — or ship a `.seb` file, which overrides them.

---

## What was changed

The upstream SEB verifies the quit password **locally**, by comparing a SHA-256 hash that is baked
into the `.seb` configuration file (`hashedQuitPassword`) with the hash of what the student typed.
That method has a drawback for a centrally managed CBT setup: changing the quit password requires
editing and re-distributing the configuration to every client.

This fork adds an alternative verification path: when a **CBT kiosk URL** is configured, SEB fetches
the current password from that endpoint and compares it directly.

### New setting keys

| Key                      | Type   | Description                                                                 |
|--------------------------|--------|-----------------------------------------------------------------------------|
| `cbtKioskURL`            | string | URL of the CBT kiosk settings endpoint (default: the built-in CBT endpoint). |
| `cbtKioskTimeout`        | int    | Request timeout in milliseconds (default `5000`).                            |
| `cbtFallbackURL`         | string | Optional secondary endpoint queried when the primary one is unreachable.     |
| `cbtKioskAttempts`       | int    | Number of attempts per endpoint (default `3`).                               |
| `cbtKioskAttemptInterval`| int    | Delay between attempts in milliseconds (default `1000`).                     |

When `cbtKioskURL` is set, the quit/unlock password is validated against the endpoint. When it is not
set, behaviour is exactly as upstream (local hash comparison).

### Endpoint contract

SEB expects the endpoint to return JSON in this shape:

```json
{
  "success": true,
  "data": {
    "exit_password": "…",
    "password_expires_at": "2026-06-28T23:55",
    "is_expired": false
  }
}
```

Behaviour:

- `success != true` or missing `data` → treated as a failure.
- `is_expired == true` → the password is rejected with a dedicated **"password expired"** message.
- If `is_expired` is false but `password_expires_at` lies in the past, the password is **also**
  rejected as expired (defensive check in case the panel forgets to set the flag).
- `exit_password` empty/absent → rejected as *unavailable*.
- Otherwise the typed password is compared to `exit_password` (case-sensitive, exact match).

The client distinguishes three failure reasons in its message box, localised in all 14 bundled
languages: **wrong password**, **expired password**, and **server unreachable**.

### Fallback behaviour

Verification tries the endpoints in this order, each retried `cbtKioskAttempts` times:

1. the primary `cbtKioskURL`,
2. the `cbtFallbackURL` (if configured) — the **online fallback**,
3. only if **no endpoint** is reachable, the **offline fallback** (see below).

#### Offline fallback (settable directly on the client)

When the CBT server cannot be reached at all, SEB checks for an emergency quit password configured
locally on the exam client — no configuration file required. It looks for a file named
**`CbtFallback.txt`** in:

1. `%ProgramData%\SafeExamBrowser\CbtFallback.txt` (all users, needs administrator rights — preferred),
2. `%APPDATA%\SafeExamBrowser\CbtFallback.txt` (current user).

The file contains either a hash or a plain-text password (hash preferred):

```
# comment lines start with '#'
password_hash=<Base16 SHA-256 hash of the emergency password>
# or
password=MyEmergencyPassword
```

See `examples/CbtFallback.txt.example`. If no such file exists, the compile-time
`CbtDefaults.OfflineFallbackPasswordHash` is used; if that is empty too, the `hashedQuitPassword` of
the active configuration applies; if none is set, access is denied.

### Security notes

- The endpoint is contacted over HTTPS. The password travels in the response body in clear text,
  protected only by TLS. **Secure the endpoint** (authentication token, IP allow-list, or a shared
  secret) — as of writing, the default endpoint is publicly readable.
- The password is never written to disk or to the `.seb` file by this integration.
- Requests are logged (without the password) through the normal SEB logging mechanism.

---

## Files added / modified

**Added**

- `SafeExamBrowser.Server.Contracts/Data/KioskSettings.cs` — response model.
- `SafeExamBrowser.Server.Contracts/ICbtKioskClient.cs` — client contract.
- `SafeExamBrowser.Server/CbtKioskClient.cs` — HTTP client + JSON parsing.
- `tools/generate-seb-config.py` — generates an unencrypted `.seb` config for CBT.
- `examples/cbt-exam-config.seb` — ready-to-use example configuration.

**Modified**

- `SafeExamBrowser.Settings/Security/SecuritySettings.cs` — new `CbtKioskUrl`, `CbtKioskTimeout`.
- `SafeExamBrowser.Configuration/ConfigurationData/Keys.cs` — config keys.
- `SafeExamBrowser.Configuration/ConfigurationData/DataMapping/SecurityDataMapper.cs` — mapping.
- `SafeExamBrowser.Configuration/ConfigurationData/DataValues.cs` — default timeout.
- `SafeExamBrowser.Client/ClientContext.cs` — holds the kiosk client.
- `SafeExamBrowser.Client/CompositionRoot.cs` — wires it up.
- `SafeExamBrowser.Client/Responsibilities/ClientResponsibility.cs` — **core change**: quit/unlock
  password verification (`IsValidQuitPassword`), plus `HasQuitPassword`.
- `SafeExamBrowser.Client/Responsibilities/ShellResponsibility.cs` — uses `HasQuitPassword`.
- `SafeExamBrowser.Client/Responsibilities/BrowserResponsibility.cs` — uses `HasQuitPassword`.
- `SafeExamBrowser.Client/Responsibilities/IntegrityResponsibility.cs` — uses `HasQuitPassword`.
- Three `.csproj` files — register the new source files.

---

## Building

SEB is a **.NET Framework 4.8 / WPF** application, so it can only be built on **Windows**.

### Option A — GitHub Actions (recommended, free)

Push to your fork; `.github/workflows/build.yml` builds the solution and uploads:

- `SEB-CBT-Setup-x64` / `SEB-CBT-Setup-x86` → platform MSI installers
- `SEB-CBT-SetupBundle` → the combined `SetupBundle.exe` (installs x64 or x86 as appropriate)
- `SEB-CBT-Client-x64` → the raw client binaries

Download the artifacts from the workflow run. The job builds x64 first, then x86, because the
bundle project packs both MSIs.

### Code signing

The upstream project signs its binaries with a certificate owned by ETH Zürich. That certificate is
not available here, so signing is **disabled by default** and the produced installers are unsigned.
Windows SmartScreen will therefore warn on first run — click *More info → Run anyway*, or sign the
build yourself.

To sign with your own certificate, pass:

```
msbuild SafeExamBrowser.sln /p:Configuration=Release /p:Platform=x64 ^
  /p:SignInstallers=true /p:SigningCertificateSha1=<your-cert-thumbprint>
```

(The certificate must be present in the build machine's certificate store.)

### Option B — local Windows machine

Requirements:

- Visual Studio 2022 (workload ".NET desktop development")
- .NET Framework 4.8 Developer Pack
- Visual C++ 2015–2022 Redistributable
- WiX Toolset v3.14 (only needed for the installer project)

```
nuget restore SafeExamBrowser.sln
msbuild SafeExamBrowser.sln /p:Configuration=Release /p:Platform=x64 /p:langversion=latest
```

---

## Configuration

Generate the example configuration (requires Python 3):

```
python3 tools/generate-seb-config.py examples/cbt-exam-config.seb
```

The generated file:

- locks the client to `https://cbt.smkdata.sch.id` (`startURL`),
- points at the CBT kiosk endpoint (`cbtKioskURL`),
- blocks the usual escape hatches (Alt+Tab, Alt+F4, clipboard, printing, downloads, …),
- restricts URL navigation to the CBT host.

To customise, edit the constants at the top of `tools/generate-seb-config.py` and regenerate.

> **Note:** The example is saved as an *unencrypted* (`plnd`) file so it is easy to inspect and edit.
> For production you should save it *encrypted* with a settings password using the official
> SEB Config Tool, and consider signing the build.

---

## Licensing (MPL-2.0)

SEB is licensed under the **Mozilla Public License 2.0**. This fork keeps that license. If you
distribute a modified version, the modified source files must remain available under the MPL-2.0 —
see `LICENSE.txt`. Using it internally within your own institution does not trigger distribution
obligations.
