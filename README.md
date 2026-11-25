<h1 align="center">📊 Game Report</h1>

<p align="center">
  <strong>Công cụ Unity Editor xem thành phần của bản build gần nhất.</strong>
  <br/>
  Asset nặng, nhóm file và cấu trúc APK/AAB/IPA trong một cửa sổ gọn.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3+-000000?logo=unity&logoColor=white&style=flat-square">
  <img src="https://img.shields.io/badge/UPM-Git%20Package-2C8EBB?style=flat-square">
  <img src="https://img.shields.io/badge/Package-com.dann.gamereport-6A5ACD?style=flat-square">
  <img src="https://img.shields.io/badge/Editor-only-2EA44F?style=flat-square">
  <img src="https://img.shields.io/badge/Owner-PocketPlay-FF6B6B?style=flat-square">
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/Changelog-1.0.2-0366D6?style=flat-square"></a>
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
| **Summary** | Platform, thời điểm build xong, kích thước file output, tổng asset và số file, thành phần bên trong APK/AAB/IPA, size theo loại asset, 10 asset nặng nhất |
| **Files** | Toàn bộ asset trong build. Ô tìm kiếm khớp path hoặc loại asset. Sort theo `Size` (mặc định), `Type` hoặc `Path`. Cột: size, phần trăm, loại, path |
| **Groups** | Gộp asset theo thư mục (2 cấp đầu của path) hoặc theo loại asset. Bấm một nhóm để mở tab Files đã lọc sẵn theo nhóm đó |

Thanh công cụ có `Refresh`. Khi project có báo cáo của nhiều platform, thanh công cụ có thêm dropdown chọn platform.

Asset từ 500 KB trở lên được tô đỏ trong mọi danh sách. Ngưỡng này cố định trong code (`HeavyThresholdBytes`).

Thao tác trên một dòng asset:

| Thao tác | Kết quả |
| --- | --- |
| Bấm trái | Ping asset trong Project window và chọn dòng |
| Bấm đúp hoặc nút `Select` | Chọn asset trong Project |
| Bấm phải | Menu `Copy Path`, `Select in Project` |

## Nơi lưu báo cáo

Báo cáo ghi vào `Library/BuildReportWindow/<Platform>.json`, ví dụ `Android.json`. Mỗi platform một file, build mới ghi đè build cũ cùng platform; build Android và iOS không đè nhau. Thư mục `Library` nằm ngoài Git nên báo cáo chỉ tồn tại trên máy build. Cache của project khác bị từ chối khi đọc.

Package ghi qua `IPostprocessBuildWithReport` với `callbackOrder = int.MaxValue`.

## Khi Unity không trả danh sách asset

Build tăng dần (incremental) có thể trả `packedAssets` rỗng. Khi đó package giữ lại danh sách asset của lần build trước cùng platform và hiện cảnh báo vàng ở tab Summary kèm thời điểm của danh sách đó. Số liệu output file (kích thước, thành phần APK/AAB/IPA) vẫn là của lần build mới nhất. Chạy clean build để có danh sách asset đúng của build hiện tại.

Nếu chưa có danh sách nào từ trước, cảnh báo ghi rõ Unity không trả danh sách asset cho build này.

## Giới hạn

- Kích thước asset do Unity báo là **chưa nén**, khác với kích thước thực trong APK/AAB/IPA. So sánh tổng asset với dòng `Unity data` trong bảng **Inside the Output File** ở tab Summary.
- Phân tích output chỉ hỗ trợ file `.apk`, `.aab`, `.ipa`. File Zip64 chưa hỗ trợ.
- File `.cs` bị bỏ qua vì code được biên dịch thành native, entry trong báo cáo chỉ là MonoScript nhỏ. Size code chỉ thấy gộp trong `Native libs` và `Java/Kotlin code`.
- Chưa đọc riêng bundle Addressables. Kích thước asset nằm trong bundle có thể không xuất hiện ở danh sách asset.
- Chỉ giữ báo cáo của lần build gần nhất mỗi platform, chưa có so sánh giữa các build.
- Chưa hiển thị scene nào tham chiếu asset.

## Cấu trúc

```text
Editor/
  Dann.GameReport.Editor.asmdef   assembly chỉ cho Editor
  BuildReportWindow.cs            cửa sổ, ghi báo cáo sau build, đọc cấu trúc zip
```

`BuildReportCacheWriter` (cùng file) nhận callback post-build và gọi `BuildReportWindow.SaveReport`. Giữ các file `.meta` trong Git để bảo toàn GUID của asset.

## License

MIT, xem [LICENSE.md](LICENSE.md).
