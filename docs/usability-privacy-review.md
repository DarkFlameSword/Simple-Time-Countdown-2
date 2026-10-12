# Usability, accessibility and privacy review

[**English**](usability-privacy-review.md) | [**简体中文**](usability-privacy-review.zh-CN.md)

This document checks Simple Time Countdown against widely used usability, accessibility and privacy guidelines, and records what was changed as a result. It is a self-assessment by the project, not a certification.

- **Usability**: Jakob Nielsen's 10 usability heuristics; ISO 9241-110:2020 (interaction principles); ISO 9241-11:2018 (usability: effectiveness, efficiency, satisfaction).
- **Accessibility**: ISO 9241-171 and WCAG 2.2 (ISO/IEC 40500 is WCAG 2.0).
- **Privacy and permissions**: ISO/IEC 29100 (privacy framework) and the practices Google publishes for apps (request only the permissions you need, ask in context, handle refusal, keep sensitive content off the lock screen, minimize and disclose data). Google's guidance is written for Android and the web; it is applied here to a Windows desktop app.

Items marked **New** were added in this review. The others were already in place.

## 1. Nielsen's 10 usability heuristics

| # | Heuristic | How the app meets it |
| --- | --- | --- |
| 1 | Visibility of system status | Every card counts down live with a status badge (Standing, Urgent, Perilous, Overdue). The footer shows how many countdowns are listed, and a save problem with "Retrying…". **New**: after archive, restore or delete, the footer says what happened ("Archived “Thesis”."). **New**: the editor says when the chosen time has already passed. |
| 2 | Match between system and the real world | Plain wording, days/hours/minutes, the user's regional date format and localized time-zone names. Daylight-saving gaps and repeated hours are explained in words, not offsets alone. |
| 3 | User control and freedom | **New**: archive, restore and delete can be undone from the footer or with `Ctrl+Z`. **New**: the editor asks before discarding typed input on `Esc`, **Cancel** or `Alt+F4`. **New**: `Esc` clears and then closes the search from anywhere in the panel. Settings apply live and are left with **DONE**, so nothing has to be confirmed. |
| 4 | Consistency and standards | Windows conventions: `Ctrl+N`, `Ctrl+F`, `Ctrl+E`, **New** `Ctrl+Z` and `F1`; `Enter` saves, `Esc` cancels; the tray icon opens on a single click. One style each for icon buttons, text links and primary actions. |
| 5 | Error prevention | Required-field marker, length limits with counters from 80% full, a supported date range, and a refused daylight-saving gap. The panel's 5,000-countdown limit is checked before the editor opens, so no typing is lost. **New**: the delete question uses a warning icon, defaults to **No**, and suggests archiving instead. **New**: a past due time is pointed out before saving. |
| 6 | Recognition rather than recall | Every icon button has a name and a tooltip that includes its shortcut. Empty states say what to do next (**New**: "Choose + or press Ctrl+N", "Press Esc to clear the search"). |
| 7 | Flexibility and efficiency of use | Keyboard shortcuts, `Home`/`End`/`Page Up`/`Page Down` across cards, Add countdown in the tray menu, default reminder and time zone, pinning. |
| 8 | Aesthetic and minimalist design | One paper sheet, one accent colour. Card actions stay quiet until the card is hovered or focused. Notices appear only while they have something to say. |
| 9 | Help users recognize, diagnose and recover from errors | Validation messages sit under the field, and focus moves to the first field in error. An unreadable save file is restored from its backup and the original kept. Failed saves retry. **New**: deletes can be undone, and an empty search explains how to get back. |
| 10 | Help and documentation | **New**: `F1` and **Settings > About > User guide** open the user guide in the UI language. Bilingual README with a troubleshooting section. |

## 2. ISO 9241-110:2020 interaction principles

| Principle | Evidence |
| --- | --- |
| Suitability for the user's tasks | A countdown is created in one dialog with sensible defaults (tomorrow, the default reminder and time zone). The panel can float above other windows, stay behind them, or wait in the tray. |
| Self-descriptiveness | Labels, units and status words on every card. Tooltips name the action and the card. Settings explain their effect in a caption (desktop layer, opacity in a contrast theme, **New** notification titles). |
| Conformity with user expectations | Standard Windows keys and dialogs. The close button's tooltip says whether it closes to the tray or exits. Pinned cards sort first, then by deadline. |
| Learnability | Shortcuts are shown in tooltips. **New**: hints in the empty states, `F1` for the user guide. |
| Controllability | The user decides panel position, size, opacity, layer, language, reminders, thresholds and autostart. **New**: undo and discard confirmation. Sliders and readouts work with the keyboard. |
| Use error robustness | Inline validation, a state file that is never overwritten before it is safely kept, and atomic saves with a backup. **New**: undo, a delete question that defaults to **No**, and a discard question. |
| User engagement | A distinctive "casebook" look and calm visuals (no pop-ups beyond the user's reminders). |

ISO 9241-11 measures usability as effectiveness, efficiency and satisfaction in a context of use. No formal user test has been run yet. A suggested protocol is in [section 6](#6-known-limitations-and-next-steps).

## 3. Accessibility (ISO 9241-171, WCAG 2.2)

- Everything works from the keyboard with a visible focus ring (WCAG 2.1.1, 2.4.7).
- Screen readers get a one-sentence summary of each card, names for every control, slider values with their units, and live announcements for counters, validation, save problems and (**New**) the undo notice (WCAG 4.1.2, 4.1.3).
- Windows contrast themes are followed, and text colours meet AA contrast at every panel opacity (WCAG 1.4.3, 1.4.11).
- Text is never smaller than 12 DIP, follows the Windows text-size setting and wraps rather than clips (WCAG 1.4.4, 1.4.10).
- **New**: the undo offer never lapses while the pointer or keyboard focus is on it, so it can be reached without hurrying (WCAG 2.2.1 Timing adjustable).

## 4. Privacy and permissions

### ISO/IEC 29100 privacy principles

| Principle | How the app meets it |
| --- | --- |
| Consent and choice | Launch at startup is off until the user turns it on; the MSIX startup task ships disabled. **New**: notifications can leave countdown titles out. The uninstaller asks whether to delete the user's data. |
| Purpose legitimacy and specification | Data is used only to show countdowns and send the user's own reminders. **New**: Settings > Privacy & data says so. |
| Collection limitation | Only what the user types and the panel's settings. No account, device identifiers or usage data. |
| Data minimization | Logs never contain countdown text. **New**: the user's profile folder (with the account name) is written as `%USERPROFILE%` in logs. **New**: notifications without titles. |
| Use, retention and disclosure limitation | Nothing is sent anywhere; the app has no network code. Logs are kept 7 days; at most 5 copies each of unreadable and of trimmed state files are kept. |
| Accuracy and quality | Input is normalized and validated; saved files are repaired on load and written atomically with a backup. |
| Openness, transparency and notice | **New**: a plain statement in Settings > Privacy & data. The README's "Your data and privacy" section, and this document. |
| Individual participation and access | **New**: **Open the data folder** shows `state.json` (readable JSON) for backup or moving to another PC; MSIX builds open their private storage. Countdowns can be deleted one by one, or all data on uninstall. |
| Accountability | **New**: CONTRIBUTING lists the privacy rules for future changes, and a test checks the log redaction. |
| Information security | Per-user folders with Windows' default per-user permissions; atomic writes; the app runs as the normal user; per-user install with no administrator rights; CI with read-only tokens and pinned actions. |
| Privacy compliance | No third-party SDKs or analytics; the .NET runtime's notices ship with the app. |

### Permission practices (after Google's guidance)

| Practice | How the app follows it |
| --- | --- |
| Request only what you need | `asInvoker` manifest (never elevated); per-user install; the MSIX declares only `runFullTrust`, which every packaged desktop app needs. |
| Ask in context, when the feature is used | Autostart is requested only when the user ticks **Launch at startup**. Notifications use the tray icon and need no extra prompt. |
| Explain why | Each sensitive setting has a caption explaining what it does. |
| Handle refusal gracefully | If Windows refuses autostart, the box snaps back with a reason. If the desktop layer is refused, the panel falls back to a normal window and says so. Notifications respect Focus / Do not disturb. |
| Let users revoke | Every choice can be undone in Settings or in Windows Settings > Apps > Startup (linked for MSIX). |
| Keep sensitive content off the lock screen | **New**: **Show countdown titles in notifications** can be turned off. |
| Minimize and protect what is logged | **New**: profile-path redaction; logs are local and short-lived. |

## 5. Changes made in this review

| Change | Guidelines | Where |
| --- | --- | --- |
| Undo for archive, restore and delete (footer notice + `Ctrl+Z`) | Nielsen 1, 3, 9; ISO 9241-110 controllability, use error robustness; WCAG 2.2.1 | `MainWindowViewModel.Undo`, `MainWindow.xaml(.cs)` |
| Delete question: warning icon, default **No**, suggests archiving | Nielsen 5; use error robustness | `MainWindow.DeleteCountdown_Click` |
| Editor asks before discarding typed input | Nielsen 3, 5 | `EditCountdownWindow.OnClosing`, `EditCountdownViewModel.HasChanges` |
| Editor points out a due time that has passed | Nielsen 1, 5 | `EditCountdownViewModel.UpdateScheduleNote` |
| Option to leave titles out of notifications | ISO/IEC 29100 consent, minimization; lock-screen guidance | `AppSettings.ShowTitlesInNotifications`, `MainWindowViewModel` |
| Settings > Privacy & data: statement, open data / log folder (MSIX-aware) | ISO/IEC 29100 openness, access | `SettingsWindow.xaml`, `AboutInfo`, `PackageInfo.ResolveForShell` |
| Log redaction of the user profile folder | ISO/IEC 29100 minimization | `AppLog.Redact` |
| `F1` / User guide; `Esc` clears search anywhere; recovery hints in empty states | Nielsen 6, 9, 10; ISO 9241-110 learnability | `MainWindow.xaml.cs`, `LocalizationService` |
| Tests for undo, notification privacy, discard tracking, the past-time note and redaction | Accountability | `tests/` |

## 6. Known limitations and next steps

- Undo covers archive, restore and delete, not edits or pinning. Edits are already reversible by editing again, and the editor protects unsaved input.
- There is no in-app export, import or "delete all data" button. The data folder button, per-card delete and the uninstaller's data option cover access and erasure.
- A notification without a title still shows the app's name; whether it appears on the lock screen at all is controlled by Windows' notification settings.
- Usability has not been measured with users yet (ISO 9241-11). A small test with 5–8 participants covering "add a countdown with a reminder", "find and edit one", "archive and get it back" and "hide titles from notifications" would give task success, time on task and a SUS score to track over releases.
