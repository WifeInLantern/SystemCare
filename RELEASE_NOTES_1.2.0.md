# SystemCare 1.2.0

A Windows maintenance suite for cleaning, optimizing, analyzing and protecting your PC — now with software updating, automatic maintenance and diagnostic logging.

## ✨ New in 1.2.0

- **Software Updater (winget)** — Check for and apply updates to all your installed apps in one place. Pick exactly what to update, **Ignore** apps you never want touched, or **Update all** at once. A restore point is created first and every result is logged.
- **Diagnostic logging** — SystemCare now keeps rolling daily logs (maintenance runs, app updates, update checks and unexpected errors) under `%AppData%\SystemCare\logs`. Open them anytime from **Settings → About → Open logs folder**. Logs older than 14 days are pruned automatically.
- **Smarter PC Health score** — The dashboard health score now also factors in your **security posture** (Defender, firewall, UAC, Remote Desktop, Windows Update), alongside junk, startup load and memory pressure.
- **Automatic maintenance** — Schedule junk cleanup + memory trim to run **daily or weekly** via a Windows scheduled task, with a restore point beforehand and tray notifications. Configure it in **Settings → Automatic maintenance**.
- **Per-component temperatures** in System Info (CPU / GPU / drives, via LibreHardwareMonitor).
- **Leftover finder in the Uninstaller** — after removing a program, SystemCare scans for and offers to clean leftover files, folders and registry entries (with safety guards against generic-name false matches).
- **Activity History** — a running log of what SystemCare cleaned, freed and updated.
- **Built-in updater** — SystemCare checks GitHub on startup, downloads new versions and offers to install them. Private-repo support via an optional token in **Settings → Updates**.

## 🧰 Core features

- Junk Cleanup, Privacy Cleaner, Duplicate Finder, File Shredder, Registry Cleaner, Empty Folder finder
- One-click Boost, Startup Manager, Windows Tweaks, Disk Health, Deep Cleanup, Driver Updater
- Disk Analyzer, Process & Service manager, Network Tools, System Info & live monitor
- Bloatware removal, Security Checkup, Rescue Center (restore points), Software Uninstaller

## 📥 Install

1. Download **`SystemCare-Setup.exe`** below.
2. Run it (administrator rights are required — SystemCare touches system-wide locations).
3. Launch SystemCare from the Start menu or desktop shortcut.

To update from an earlier build, just run the new installer — it upgrades in place.

## ⚙️ Requirements

- Windows 10 / 11 (x64)
- Administrator rights
- The **Software Updater** needs the Windows Package Manager (**App Installer** / winget); SystemCare shows guidance if it's missing.

---
**Note:** This is an unsigned installer, so Windows SmartScreen may warn on first run — choose *More info → Run anyway*.
