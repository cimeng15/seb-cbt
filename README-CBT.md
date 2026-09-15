# Safe Exam Browser — CBT Edition

Fork of [Safe Exam Browser for Windows](https://github.com/SafeExamBrowser/seb-win-refactoring) that
integrates with the CBT panel at **https://cbt.smkdata.sch.id**.

The goal: the exam administrator manages the **quit/unlock password** centrally from the CBT panel,
and every running exam client picks it up automatically — no need to regenerate and redistribute a
`.seb` configuration file whenever the password changes.

---

## What was changed

The upstream SEB verifies the quit password **locally**, by comparing a SHA-256 hash that is baked
into the `.seb` configuration file (`hashedQuitPassword`) with the hash of what the student typed.
That method has a drawback for a centrally managed CBT setup: changing the quit password requires
editing and re-distributing the configuration to every client.

This fork adds an alternative verification path: when a **CBT kiosk URL** is configured, SEB fetches
the current password from that endpoint and compares it directly.

### New setting keys

| Key                | Type   | Description                                                                 |
|--------------------|--------|-----------------------------------------------------------------------------|
| `cbtKioskURL`      | string | URL of the CBT kiosk settings endpoint (e.g. `https://cbt.smkdata.sch.id/api/kiosk/settings`). |
| `cbtKioskTimeout`  | int    | Request timeout in milliseconds (default `5000`).                           |

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
- `is_expired == true` → the password is rejected (the exam cannot be quit with it).
- `exit_password` empty/absent → rejected.
- Otherwise the typed password is compared to `exit_password` (case-sensitive, exact match).

### Fallback behaviour

If the endpoint cannot be reached (offline, server down, timeout), SEB logs a warning and falls back
to the locally configured `hashedQuitPassword`, **if one is set**. If no local hash is configured,
access is denied. This guarantees that an exam can still be terminated even when the CBT server is
unavailable — configure a local `hashedQuitPassword` as an emergency password if you need this.

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

- `SEB-CBT-Setup-<platform>` → the `Setup.msi` installer
- `SEB-CBT-Client-<platform>` → the client binaries

Download the artifacts from the workflow run.

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
