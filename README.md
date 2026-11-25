<h1 align="center">📊 Game Report</h1>

<p align="center">
  <strong>Công cụ Unity Editor xem thành phần của bản build gần nhất.</strong>
  <br/>
  Asset nặng, nhóm file, cấu trúc APK/AAB/IPA và scene nào kéo asset vào build.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3+-000000?logo=unity&logoColor=white&style=flat-square">
  <img src="https://img.shields.io/badge/UPM-Git%20Package-2C8EBB?style=flat-square">
  <img src="https://img.shields.io/badge/Package-com.dann.gamereport-6A5ACD?style=flat-square">
  <img src="https://img.shields.io/badge/Editor-only-2EA44F?style=flat-square">
  <img src="https://img.shields.io/badge/Owner-PocketPlay-FF6B6B?style=flat-square">
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/Changelog-1.0.1-0366D6?style=flat-square"></a>
</p>

---

Package ghi báo cáo ngay sau mỗi lần build rồi hiển thị trong một cửa sổ Editor. Không có code runtime, không vào build.

**Yêu cầu:** Unity 2021.3 trở lên. Package ID: `com.dann.gamereport`.

## Bắt đầu nhanh

1. Trong Unity, mở **Window → Package Manager → + → Add package from git URL...** và nhập:

   ```text
   https://github.com/PocketPlay-Interactive/_GameReport.git
   ```

   Repository private cần Git credentials phù hợp. Với bản local, chọn **Add package from disk...** và chọn `package.json`.
2. Build project một lần (Android, iOS hoặc platform khác).
3. Mở **GameFoundation → Build Report**. Cửa sổ tự nạp báo cáo của lần build vừa xong và tự cập nhật khi có build mới.

## Các tab

| Tab | Nội dung |
| --- | --- |
| **Summary** | Platform, kết quả, thời gian build, kích thước file output, tổng asset, thành phần bên trong APK/AAB/IPA, size theo loại asset, top 20 asset nặng nhất |
| **Files** | Toàn bộ asset trong build. Tìm theo path hoặc loại asset, lọc theo đuôi file, chỉ hiện file nặng, sort theo size, đuôi, loại, path |
| **Groups** | Gộp asset theo thư mục (chỉnh độ sâu 1–6), theo đuôi file hoặc theo loại asset. Bấm một nhóm để mở danh sách file của nhóm đó |

Thanh công cụ có `Refresh`, `Show Report File` và ô **Heavy threshold (KB)**. Asset từ ngưỡng này trở lên được tô đỏ (mặc định 500 KB, lưu trong `EditorPrefs`). Khi project có báo cáo của nhiều platform, thanh công cụ có thêm dropdown chọn platform.

Thao tác trên một dòng asset:

| Thao tác | Kết quả |
| --- | --- |
| Bấm trái | Ping asset trong Project window và chọn dòng |
| Bấm đúp hoặc nút `Select` | Chọn asset trong Project |
| Bấm phải | Menu `Copy Path`, `Select in Project` |

## Scene nào kéo asset vào build

Chọn một dòng asset, panel ở cuối cửa sổ liệt kê tối đa 8 scene tham chiếu asset đó. Tab **Files** có thêm cột `Scn` là số scene tham chiếu.

Dữ liệu này chỉ có khi build bật `BuildOptions.DetailedBuildReport`. Nếu không bật, cột `Scn` ẩn và panel hiện hướng dẫn. Đường dẫn scene hiển thị đúng như Unity trả về, package không tự phân tích chuỗi này.

```csharp
BuildPlayerOptions options = new BuildPlayerOptions
{
    scenes = scenes,
    locationPathName = path,
    target = BuildTarget.Android,
    options = BuildOptions.DetailedBuildReport
};
BuildPipeline.BuildPlayer(options);
```

## Nơi lưu báo cáo

Báo cáo ghi vào `Library/BuildReportWindow/<Platform>.json`, ví dụ `Android.json`. Mỗi platform một file, build mới ghi đè build cũ cùng platform; build Android và iOS không đè nhau. Thư mục `Library` nằm ngoài Git nên báo cáo chỉ tồn tại trên máy build. Cache của project khác bị từ chối khi đọc.

Package ghi qua `IPostprocessBuildWithReport` với `callbackOrder = int.MaxValue`.

## Khi Unity không trả danh sách asset

Build tăng dần (incremental) có thể trả `packedAssets` rỗng. Khi đó package giữ lại danh sách asset của lần build trước cùng platform và hiện cảnh báo vàng ở tab Summary kèm thời điểm của danh sách đó. Số liệu output file (kích thước, thành phần APK/AAB/IPA) vẫn là của lần build mới nhất. Chạy clean build để có danh sách asset đúng của build hiện tại.

Nếu chưa có danh sách nào từ trước, cảnh báo ghi rõ Unity không trả asset nào cho build này.

## Giới hạn

- Kích thước asset do Unity báo là **chưa nén**, khác với kích thước thực trong APK/AAB/IPA. Xem bảng **Output File Contents** ở tab Summary để so sánh với dòng `Unity data`.
- Phân tích output chỉ hỗ trợ file `.apk`, `.aab`, `.ipa`. File Zip64 chưa hỗ trợ.
- File `.cs` bị bỏ qua vì code được biên dịch thành native, entry trong báo cáo chỉ là MonoScript nhỏ. Size code chỉ thấy gộp trong `Native libs` và `Java/Kotlin code`.
- Chưa đọc riêng bundle Addressables. Kích thước asset nằm trong bundle có thể không xuất hiện ở danh sách asset.
- Cột `Scn` và panel scene cần `BuildOptions.DetailedBuildReport` như mục trên.
- Chỉ giữ báo cáo của lần build gần nhất mỗi platform, chưa có so sánh giữa các build.

## Cấu trúc

```text
Editor/
  Dann.GameReport.Editor.asmdef   assembly chỉ cho Editor
  BuildReportWindow.cs            cửa sổ, ghi báo cáo sau build, đọc cấu trúc zip
```

`BuildReportCacheWriter` (cùng file) nhận callback post-build và gọi `BuildReportWindow.SaveReport`. Giữ các file `.meta` trong Git để bảo toàn GUID của asset.

## License

MIT, xem [LICENSE.md](LICENSE.md).
