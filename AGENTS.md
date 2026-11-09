# EduCast project instructions

- The Git remote for EduCast is `https://github.com/WealthGate/EduCast.git`.
- Use the .NET 10 Windows SDK. Build `EduCast.slnx` and run `tests/classroom/EduCast.Checks.csproj` before shipping.
- Keep the student Program projection isolated from teacher-only content.
- For a shipping update, bump the three-part version in `Directory.Build.props` and add the user-visible changes to `CHANGELOG.md`. Push the source commit and matching annotated `v` tag. The GitHub Windows workflow publishes the tested packages.
- Keep generated installers and binaries out of Git. GitHub Releases holds versioned distribution assets.
- Follow [the release instructions](docs/RELEASES.md) and verify the published assets after the workflow finishes.
