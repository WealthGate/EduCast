# Releases and update notifications

## For teachers and testers

Get the current Windows MSI or portable ZIP from [EduCast Releases](https://github.com/WealthGate/EduCast/releases). To receive GitHub notifications, open the repository and choose **Watch → Custom → Releases**. GitHub uses the delivery preferences in your account. [GitHub notification settings](https://docs.github.com/en/subscriptions-and-notifications/get-started/configuring-notifications#configuring-your-watch-settings-for-an-individual-repository)

EduCast 0.3.0 and later check for a newer published Windows installer at startup. Choose **Check for updates** to check again. When the toolbar shows **Download v…**, select it to open the matching GitHub installer download. Save and close your lesson before running the downloaded MSI. A portable user can instead download and extract the new ZIP. Both packages require the Windows x64 .NET 10 Desktop Runtime. The preview MSI is unsigned and runs only when you start it yourself.

The update request goes to the public GitHub release API. The check runs in the background. Classroom features work offline, and no account or lesson data is sent by the check. Downloads open in the system browser. [GitHub latest release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)

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

4. The Windows workflow checks that the tag matches the source version. It builds and tests the project, creates the MSI and ZIP, and publishes them with `SHA256SUMS.txt`. The publish job has write permission for Releases. Ordinary branch and pull-request builds produce a preview artifact for developers.
5. Confirm the run and the assets on the published release page. If the run fails before publication, diagnose it in GitHub Actions. Fix the issue in a new commit and version when a source change is needed.

The shared classroom release feed follows GitHub's latest published non-prerelease. A separate tester channel and signed automatic installation are later release work.
