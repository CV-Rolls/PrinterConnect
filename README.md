# PrinterConnect

A fast, modern Windows 11 tool for finding and installing printers from company
print servers — built for end users, loved by IT.

![PrinterConnect](logo.png)

## Why

If your users see **"Windows cannot connect to the printer"** with errors
**0x0000011b, 0x0000007c, 0x00000bcb, 0x00000bbb or 0x00000709**, get admin
credential prompts when installing a shared printer, or lost the ability to
browse the print server since
the **PrintNightmare** hardening (KB5005565 and successors, RPC authentication
changes, Windows 11 24H2 SMB hardening), you know the problem: installing a
printer from your own print server became a support ticket.
PrinterConnect gives users a clean self-service list of every printer on your
print server(s): search it, see live status and toner, double-click to install.
Point-and-Print policies still fully apply — the app never bypasses Windows
security, never elevates, and works as a standard user.

## Features

- Lists every shared printer from one or more print servers, aggregated
- Instant search (umlaut-tolerant), sortable columns, saved column layout
- Live status (Ready / Printing / Paper jam / Offline …) with direct device
  reachability checks
- Toner and paper-tray levels, model, serial, lifetime page count via read-only
  SNMP (v2c with v1 fallback)
- One-click install (multi-select supported), remove, and default-printer selection
- Printers installed locally (Print to PDF, OneNote, USB) shown and removable
- Windows 11 look, light/dark/system theme, live accent color, 8 UI languages
- Single ~300 KB exe on .NET Framework 4.8 — nothing to install on Windows 11
- Writes nothing to disk except its own settings; no telemetry, no network
  listeners, no elevation (`asInvoker` manifest)

## Getting started

Download the latest release, place `PrinterConnect.exe` (and its `.exe.config`)
anywhere, run it, enter your print server (`\\PRINTSERVER`) once. Settings are
stored per user in `%APPDATA%\PrinterConnect\settings.json`.

## Deploying in your company

1. **Sign it with your certificate** (recommended — avoids SmartScreen and lets
   you allow-list by publisher):
   ```powershell
   .\deploy\sign.ps1 -ExePath .\PrinterConnect.exe -Thumbprint <YourCertThumbprint>
   ```
   Works with an internal CA cert, an OV/EV cert, or Azure Trusted Signing.
2. **Package for Intune** — see [`deploy/INTUNE.md`](deploy/INTUNE.md).
3. Optionally pre-seed the default print server and column layout by deploying a
   `settings.json` alongside your rollout.

## FAQ

**Will users still get admin / "Do you trust this printer?" prompts?**
PrinterConnect never bypasses Windows security — installs go through
`AddPrinterConnection2`, so your Point-and-Print policy decides. To make
installs prompt-free for standard users, use Type 4 / package-aware drivers on
the server and set the *Point and Print Restrictions* GPO to your trusted print
servers (see KB5005652). PrinterConnect then gives users the browsing and
one-click experience Microsoft removed.

**Do GPO-deployed printers conflict with this?**
No — connections made here are the same per-user connections GPP creates.
Many teams use GPO for the mandatory queues and PrinterConnect for everything
self-service.

**Why is the download unsigned?**
So your company can sign it under its *own* identity: run
`deploy\sign.ps1` with your certificate, allow-list the publisher once in
your security tools, and every future signed build is trusted automatically.

## Building from source

```
dotnet build -c Release        # requires .NET SDK 8+; on non-Windows: EnableWindowsTargeting is set
```

Output: `bin/Release/net48/PrinterConnect.exe`.

## Security posture

- Runs strictly as the invoking user; the manifest forbids elevation
- Printer installs use `AddPrinterConnection2` — Windows' own Point-and-Print
  policy checks fully apply
- SNMP is read-only (community `public`), unicast to known device addresses
- No credentials stored, no inbound sockets, no shell execution of user input

## License

MIT — see [LICENSE](LICENSE).

Free software by [ClearVantage.io](https://clearvantage.io) — developer and maintainer.
