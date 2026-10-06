# Game Report

Unity Editor tool for inspecting asset sizes and output contents from the latest build.

## Requirements

- Unity 2021.3 or newer

## Install

In Unity, open **Window > Package Manager**, select **Add package from git URL**, then enter:

```text
https://github.com/PocketPlay-Interactive/_GameReport.git
```

For local development, use:

```text
file:E:/GitHub/_GameReport
```

## Use

1. Build the Unity project once.
2. Open **Build Report** from Unity's main menu.
3. Review:
   - **Summary**: build details, output composition, asset-type totals, largest assets.
   - **Files**: search, filter, sort, and select included assets.
   - **Groups**: aggregate assets by folder, extension, or asset type.

The package writes the latest report to `Library/BuildReportWindow/latest.json`. This cache stays local to the Unity project.

## Limitations

- Output composition supports APK, AAB, and IPA files.
- Zip64 output files are not supported.
- Asset sizes reported by Unity are uncompressed and may differ from final output size.

## License

MIT. See [LICENSE.md](LICENSE.md).
