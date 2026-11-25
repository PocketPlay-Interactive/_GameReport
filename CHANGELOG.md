# Changelog

## [1.0.2] - 2026-11-25

- Simplified the window: removed the heavy threshold field, the extension filter, the `Ext` and `Scn` columns, the scene detail panel and the group-by-extension mode.
- Shortened the Summary tab (top 10 assets, fewer build detail rows, one-line warnings).
- Stopped collecting scene references.

## [1.0.1] - 2026-11-25

- Added a cache file per platform (`Library/BuildReportWindow/<Platform>.json`) and a platform dropdown.
- Added scene references per asset (`Scn` column and detail panel), available when the build uses `BuildOptions.DetailedBuildReport`.
- Kept the previous asset list and showed a warning when Unity reports no packed assets for a build.
- Rewrote the README.

## [1.0.0] - 2026-10-06

- Initial UPM release.
- Added post-build report caching.
- Added build summary, asset file, and grouped size views.
- Added APK, AAB, and IPA output composition analysis.
