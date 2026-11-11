# EduCast Studio Privacy Notice

Effective November 11, 2026

This notice describes how the EduCast Studio Windows app handles information. The app has no EduCast sign-in or cloud lesson storage. The board and local project tools work offline.

## What stays on your device

- **Lessons and exports.** You choose where to save editable `.educast` projects, PNG exports, and MP4 recordings. Editable projects include teacher notes and private or hidden content. Teaching-source annotations stay in memory for the current session. A folder synced by another service can upload files under that service’s settings.
- **Preferences and diagnostics.** EduCast stores the selected theme and any local diagnostic log under `%LOCALAPPDATA%\EduCast Studio`. The log records the time, exception type, and result code. The desktop app does not automatically upload this log. Updates also use a staging folder under Windows temporary storage.
- **Teaching browser.** EduCast uses an InPrivate WebView2 profile with an app-specific data folder. Websites can receive the usual connection information and anything you submit to them. Microsoft WebView2 can send diagnostic data to Microsoft. [Microsoft WebView2 data privacy](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/data-privacy), [Microsoft Privacy Statement](https://www.microsoft.com/en-us/privacy/privacystatement)

## What you share

**Program** is the output available to students and to recording, conferencing, and streaming workflows. Teacher notes, teacher-only objects, and unrevealed answers stay out of Program. A teaching browser page, captured app window, or camera preview can appear in Program after you enable **Show on Program**. Check the visible content of a source before sharing it, including any personal information displayed by the source itself.

A local recording can include audio from the microphone you select for that recording. When you start a live stream, EduCast sends Program video and the selected stream audio to the destinations you configure. The destination provider controls its own retention, access, and replay settings. If you share the **Online Program** window in another app, that app handles the resulting share.

## Downloads and update checks

EduCast checks GitHub’s public release metadata over HTTPS on startup and when you choose **Check for updates**. The request does not include lesson content or account details. GitHub receives standard connection information, such as your IP address, for update requests and downloads. GitHub also handles activity you choose to make on the repository, such as posting an issue. [GitHub General Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)

## Retention and privacy questions

Saved lessons, exports, recordings, preferences, and logs remain on your device until you or your organization remove them. You can delete saved files from their chosen folders and clear EduCast’s local app data after closing the app. To manage a published copy, use the relevant streaming, conferencing, or storage service’s controls.

For privacy questions or requests, open a general issue asking the EduCast maintainers for a private follow-up. Keep names, student information, files, stream keys, and other private details out of the public issue. [EduCast issues](https://github.com/WealthGate/EduCast/issues)

See the [classroom privacy and security boundary](docs/SECURITY.md) for details on Program and the [Terms and Conditions](TERMS.md) for use of the app.
