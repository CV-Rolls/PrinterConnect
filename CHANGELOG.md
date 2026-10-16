# Changelog

All notable changes to PrinterConnect. Newest first.

## 3.12.0 — 2026-09-30

- New app icon and wordmark in the ClearSource design language: blue gradient
  rounded square with the white printer and colour toner stripes; "Printer" light +
  "Connect" semibold + gradient dot
- About shows "Developed by Roland Aring"

## 3.11.4 — 2026-09-30

- Fast corner resizing. The printer list no longer keeps a full extra screen of
  rows above and below the visible ones (WPF's default), so a taller window no longer
  means up to three times the rows rebuilt on every frame. Height-only changes skip
  column fitting entirely, fitting is throttled while the edge is being dragged and
  done exactly once on release, and layout snaps to whole pixels

## 3.11.3 — 2026-09-30

- Smooth window resizing: column fitting runs at most once per frame while dragging,
  widths are only touched when they change, and the on-screen check waits until the
  window has settled. Previously every pixel of a drag could rebuild all columns

## 3.11.2 — 2026-09-30

- Fixed properly: Install/Remove could still be cut off. Column widths were each
  "at least their minimum", but the extra given to small columns was never taken back
  from the others, so rows overran the window by 30–75 px. Widths now always add up to
  the visible width, and after drawing the app measures the real right edge and
  corrects itself if anything still sticks out

## 3.11.1 — 2026-09-30

- Fixed: the right edge of each row (the test-page button beside Remove) was cut off.
  Column fitting now accounts for the rows' own padding

## 3.11.0 — 2026-09-30

Full line-by-line code review. Fixes:

- **Security:** a malformed SNMP reply from any device on the network could freeze
  the app (infinite loop). Lengths are now validated, replies must carry the request-id
  that was sent, and the parser was fuzz-tested with 20,000 random packets
- **Regression from 3.10.0:** switched-off columns showed at startup and the saved
  column order was reset — fixed
- **Settings** are saved atomically with a backup, so a crash or a second instance can
  no longer wipe the server list and layout
- **Excel export** no longer guesses numbers: serials keep leading zeros, long serials
  stay exact, decimal commas stay text; Pages and Jobs are real numbers on every
  regional format; emoji are kept
- Jobs count updates live, and after Clear queue
- A status poll can no longer undo an install or remove that just happened, and polls
  no longer pile up when a print server is slow
- Clear queue retries when jobs arrive mid-clear, and deletes every job Windows allows
- Error 0x00000bcb now explains the Point-and-Print admin block correctly
- Saved Light/Dark choice applies from the first frame; the language menu follows theme
  changes; accent buttons pick readable text colour; hint text meets contrast minimums
- Dropdowns close when their own button is clicked again; spinners stop animating when
  hidden (idle CPU); the header no longer shifts while loading
- Setting the default printer no longer freezes the window on a slow server; removing
  a printer no longer takes over a default that belongs to an unlisted server
- Arabic uses right-to-left layout; all printer statuses translated in all 14 languages
- A startup failure now exits instead of leaving an invisible process
- Release builds check that the tag matches the version; sign.ps1 fails loudly

## 3.10.0 — 2026-09-29

- **Install is always on screen.** On small laptops, 125–150 % display scaling or a
  snapped half-screen window, the grid used to overflow and cut off the Install/Remove
  column. Columns now step aside by priority (uptime, display, trays, driver … first)
  until everything fits; Printer, Status and Install/Remove are never hidden. Widen the
  window and they come back. The status bar says how many columns are hidden.
- Action column is sized to its real content, so the test-page button is no longer clipped
- Manual column widths are still respected — until they would push Install off screen

## 3.9.0 — 2026-07-26

- Attribution simplified: ClearVantage.io is named as developer and maintainer
  throughout (About dialog, README, executable metadata)

## 3.8.0

- **Clear print queue**: one click removes stuck jobs. Full queue purge with
  Manage-Documents rights, otherwise the current user's own jobs

## 3.7.0

- Column alignment choice (left or center) for cells and headers, persisted
- Windows 11 Fluent button system: accent Install, neutral secondaries,
  quiet-destructive Remove (red text, red fill on hover)
- Copy buttons confirm inline with a brief checkmark

## 3.6.0

- Smart default printer: a sole remaining printer becomes the default, and
  removing the default promotes the best remaining candidate

## 3.5.0

- **Driver type column** (Type 3 / Type 4) with KB5005652 guidance, sortable so
  problem queues surface first, included in the Excel export
- Code signing guidance added to the info screen

## 3.4.0

- Export button in the header with a completion dialog
- Excel export file fixed (control characters from SNMP strings are stripped)
- Single brand header — the duplicate window-caption title is suppressed

## 3.3.0

- **Excel export** of the full printer inventory (.xlsx, every column)
- IP links detect HTTPS before opening the device web page
- Copy buttons for IP and serial

## 3.2.0

- Print a Windows test page from any installed printer

## 3.1.0

- Movable and resizable columns with saved order
- Printer web interface opens from the IP column
- Flag-based language menu; the app follows the PC display language
- Windows 11 dark palette

## 2.19.0 and earlier

- 2.19 — auto-load on server selection, quieter Explorer-style column headers
- 2.18 — seamless Windows 11 titlebar, uniform rows, 14 UI languages
- 2.16 — Add device via the native Windows flow, full security review
- 2.14 — new app icon, default printer selection, local printers listed
- 2.13 — much faster device queries (SNMPv2c), every column sortable
- 2.11 — model, serial, page count, paper trays, display; column chooser
- 2.9 — Windows 11 visual language, live reachability checks
- 2.7 — instant start, background refresh
- 2.4 — light/dark/system theme, IP column, multi-server support
- 2.3 — multi-select install, precise error codes
- 2.0 — complete modern rebuild: search, live status, toner levels
