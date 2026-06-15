# SystemCare 1.3.0

A bug-fix and hardening release, following a full audit of the app's services and view-models.

## 🐞 Fixes

- **Restore points are now actually created.** Windows throttles restore-point creation to ~once per
  24 hours, and while throttled the call returns "success" but creates nothing - so the button looked
  like it worked but made no restore point. SystemCare now clears that throttle and **verifies a new
  restore point appeared** before reporting success (and tells you why if it genuinely can't: System
  Protection off, or no disk space reserved).
- **Your settings no longer get lost under concurrent saves.** Settings written at the same time from
  the UI, a background scan and scheduled maintenance could collide and drop a change. Saves are now
  serialized and crash-safe.
- **Boost no longer leaves apps frozen.** Boosting an already-paused app twice could leave it
  suspended after Restore; Boost now skips apps it has already paused so Restore always resumes them.
- **Safer auto-updates.** The updater only ever downloads and launches an `.exe` installer - never an
  arbitrary release asset (zip / source / checksum) - and reports "no installer attached" when a
  release has none.

## 🔒 Security (since 1.2.0)

- The updater sends your optional GitHub token only to GitHub over HTTPS, and refuses to download the
  installer over plain HTTP.

## 📥 Install

Download **`SystemCare-Setup.exe`** below and run it (administrator rights required). Upgrades install
in place over earlier versions. The installer is unsigned, so SmartScreen may warn - choose
**More info > Run anyway**.

Full history: [CHANGELOG.md](https://github.com/WifeInLantern/SystemCare/blob/main/CHANGELOG.md)
