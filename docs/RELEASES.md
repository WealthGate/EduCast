# Releases and update notifications

## For teachers and testers

Get the current Windows MSI or portable ZIP from [EduCast Releases](https://github.com/WealthGate/EduCast/releases). To receive GitHub notifications, open the repository and choose **Watch → Custom → Releases**. GitHub uses the delivery preferences in your account. [GitHub notification settings](https://docs.github.com/en/subscriptions-and-notifications/get-started/configuring-notifications#configuring-your-watch-settings-for-an-individual-repository)

Install v0.6.0 or later once to enable in-app installation. When the toolbar shows **Update to v…**, click it. EduCast downloads and verifies the MSI, prompts about any unsaved lesson, and closes to let Windows Installer apply the upgrade. EduCast reopens after success. If Windows Installer requires a reboot, EduCast asks you to restart Windows. Stop any active recording before updating.

Choose **Check for updates** to check again. Users on v0.3 through v0.5 can use **Download v…** to get the new MSI and run it once. Both the installer and ZIP require the Windows x64 .NET 10 Desktop Runtime. The MSI is unsigned.

Windows Installer removes the previous version as part of the per-user upgrade. After verifying the replacement, a portable app that used **Update** switches to the per-user installation and cleans the verified old app files from its launch folder when Windows permits it. Lesson files are separate from these app binaries. EduCast cleans completed installer staging folders on startup. GitHub keeps the older published packages available.

The check runs in the background against the public GitHub release API. Classroom features work offline, and the check sends no account or lesson data. [GitHub latest release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)

## Publish a new version

Use three-part versions such as `0.3.1`. Keep `Directory.Build.props`, the changelog entry and the `v` tag in agreement. Push source commits to the project's GitHub repository. Give each Windows build intended for distribution a new version. Leave older release assets available for verification and recovery.

1. Bump `Version` in `Directory.Build.props` and write the user-visible changes in `CHANGELOG.md`.
2. Run the classroom tests and commit the version on the shipping branch.
3. Create an annotated tag on that commit and push the commit and tag. Substitute the version you are releasing.

   ```powershell
   git tag -a v0.3.1 -m "EduCast Studio 0.3.1"
   git push origin main
   git push origin v0.3.1
   ```

4. The Windows workflow checks that the tag matches the source version. It builds the bundled media engine from pinned FFmpeg source and runs the classroom and loopback streaming checks. It publishes the MSI, ZIP, media engine source and build recipe, and `SHA256SUMS.txt`. Ordinary branch and pull-request builds produce a preview artifact for developers.
5. Confirm the run and the assets on the published release page. If the run fails before publication, diagnose it in GitHub Actions. Fix the issue in a new commit and version when a source change is needed.

The shared classroom release feed follows GitHub's latest published non-prerelease. A complete local release build removes recognized earlier packages and app binaries from the workspace's `artifacts` folder after the new packages are ready. Use `./scripts/Remove-OldBuilds.ps1 -KeepVersion 0.6.0 -WhatIf` to preview the cleanup. A separate tester channel and Authenticode signing are later release work.
