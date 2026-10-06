using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Dann.GameReport.Editor
{
public sealed class BuildReportCacheWriter : IPostprocessBuildWithReport
{
    public int callbackOrder => int.MaxValue;

    public void OnPostprocessBuild(BuildReport report)
    {
        BuildReportWindow.SaveReport(report);
    }
}

// Shows every asset included in this project's latest build, read from a project-local cache.
public class BuildReportWindow : EditorWindow
{
    [Serializable]
    private class CacheData
    {
        public int version;
        public string projectPath;
        public ReportData report;
    }

    [Serializable]
    private class ReportData
    {
        public string platform;
        public string result;
        public string buildEndedAt;
        public long totalTimeTicks;
        public string outputPath;
        public long outputFileSize = -1;
        public List<OutputPart> outputParts = new List<OutputPart>();
        public long packedTotal;
        public List<CategoryEntry> categories = new List<CategoryEntry>();
        public List<FileEntry> files = new List<FileEntry>();
    }

    [Serializable]
    private class OutputPart
    {
        public string name;
        public long compressed;
        public long uncompressed;
    }

    [Serializable]
    private class CategoryEntry
    {
        public string name;
        public long bytes;
        public float percent;
    }

    [Serializable]
    private class FileEntry
    {
        public string path;
        public long bytes;
        public float percent;
        public string extension;
        public string assetType;
        public long largestPart;
    }

    private class GroupRow
    {
        public string key;
        public int count;
        public long bytes;
    }

    private enum FileSort { Size, Extension, AssetType, Path }

    private const int CacheVersion = 1;
    private const string CacheDirectory = "Library/BuildReportWindow";
    private const string ReportPath = CacheDirectory + "/latest.json";
    private const string ThresholdPrefKey = "BuildReportWindow.ThresholdKB";
    private const string AllTypes = "All types";
    private const double AutoRefreshInterval = 2d;
    private const float SelectButtonWidth = 56f;
    private static readonly string[] TabNames = { "Summary", "Files", "Groups" };
    private static readonly string[] GroupModeNames = { "By folder", "By extension", "By asset type" };

    private static readonly Color HeavyColor = new Color(0.95f, 0.3f, 0.2f, 0.22f);
    private static readonly Color SelectedColor = new Color(0.24f, 0.48f, 0.9f, 0.35f);
    private static readonly Color StripeColor = new Color(0f, 0f, 0f, 0.08f);
    private static readonly Color BarBackColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
    private static readonly Color BarFillColor = new Color(0.3f, 0.6f, 1f, 0.8f);

    private ReportData _report;
    private DateTime _reportWriteTime;
    private double _nextAutoRefreshCheck;
    private string _status;
    private int _tab;
    private float _thresholdKB;
    private string _selectedPath;

    // Files tab
    private string _search = string.Empty;
    private string[] _typeOptions = { AllTypes };
    private int _typeIndex;
    private bool _onlyHeavy;
    private FileSort _sort = FileSort.Size;
    private bool _sortAscending;
    private List<FileEntry> _filteredFiles = new List<FileEntry>();
    private long _filteredBytes;
    private bool _filesDirty = true;
    private Vector2 _filesScroll;

    // Groups tab
    private int _groupMode;
    private int _folderDepth = 2;
    private List<GroupRow> _groups = new List<GroupRow>();
    private bool _groupsDirty = true;
    private Vector2 _groupScroll;

    private Vector2 _summaryScroll;

    private float RowHeight => EditorGUIUtility.singleLineHeight + 2f;
    private long ThresholdBytes => (long)(_thresholdKB * 1024f);

    [MenuItem("GameFoundation/Build Report")]
    public static void Open()
    {
        BuildReportWindow window = GetWindow<BuildReportWindow>("Build Report");
        window.minSize = new Vector2(640f, 360f);
        window.Show();
    }

    private void OnEnable()
    {
        _thresholdKB = EditorPrefs.GetFloat(ThresholdPrefKey, 500f);
        Refresh();
    }

    // Reload automatically when a completed build updates this project's cache
    private void OnInspectorUpdate()
    {
        if (EditorApplication.timeSinceStartup < _nextAutoRefreshCheck)
        {
            return;
        }
        _nextAutoRefreshCheck = EditorApplication.timeSinceStartup + AutoRefreshInterval;
        if (File.Exists(ReportPath) && File.GetLastWriteTimeUtc(ReportPath) != _reportWriteTime)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        _report = null;
        _status = null;
        _reportWriteTime = default;

        if (!File.Exists(ReportPath))
        {
            _status = "No cached build report found for this project. Build the project first.";
        }
        else
        {
            try
            {
                _reportWriteTime = File.GetLastWriteTimeUtc(ReportPath);
                _report = LoadReport(ReportPath);
                if (_report == null)
                {
                    _status = "Could not read BuildReport from " + ReportPath + ".";
                }
            }
            catch (Exception e)
            {
                _status = "Failed to read " + ReportPath + ": " + e.Message;
            }
        }

        if (_report != null)
        {
            _typeOptions = new[] { AllTypes }
                .Concat(_report.files.Select(f => f.extension).Distinct().OrderBy(e => e))
                .ToArray();
            _typeIndex = Mathf.Clamp(_typeIndex, 0, _typeOptions.Length - 1);
        }

        _filesDirty = true;
        _groupsDirty = true;
        Repaint();
    }

    #region Load

    private static ReportData LoadReport(string path)
    {
        CacheData cache = JsonUtility.FromJson<CacheData>(File.ReadAllText(path));
        if (cache == null || cache.version != CacheVersion || cache.report == null)
        {
            return null;
        }
        if (!string.Equals(cache.projectPath, GetProjectPath(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Cache belongs to another project. Build this project to create a new cache.");
        }
        return cache.report;
    }

    internal static void SaveReport(BuildReport report)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            CacheData cache = new CacheData
            {
                version = CacheVersion,
                projectPath = GetProjectPath(),
                report = ToReportData(report)
            };
            string tempPath = ReportPath + ".tmp";
            File.WriteAllText(tempPath, JsonUtility.ToJson(cache));
            if (File.Exists(ReportPath))
            {
                File.Replace(tempPath, ReportPath, null);
            }
            else
            {
                File.Move(tempPath, ReportPath);
            }

            foreach (BuildReportWindow window in Resources.FindObjectsOfTypeAll<BuildReportWindow>())
            {
                window.Refresh();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BuildReport] Could not save project cache: " + e.Message);
        }
    }

    private static string GetProjectPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static ReportData ToReportData(BuildReport report)
    {
        BuildSummary summary = report.summary;
        ReportData data = new ReportData
        {
            platform = summary.platform.ToString(),
            result = summary.result.ToString(),
            buildEndedAt = summary.buildEndedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            totalTimeTicks = summary.totalTime.Ticks,
            outputPath = summary.outputPath
        };
        ReadOutputFile(data);

        Dictionary<string, FileEntry> files = new Dictionary<string, FileEntry>();
        Dictionary<string, long> typeBytes = new Dictionary<string, long>();
        foreach (PackedAssets packed in report.packedAssets)
        {
            foreach (PackedAssetInfo info in packed.contents)
            {
                string assetPath = string.IsNullOrEmpty(info.sourceAssetPath) ? "(unknown)" : info.sourceAssetPath;
                // Scripts are compiled into native code, their entries here are tiny MonoScript stubs
                if (assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string typeName = info.type != null ? info.type.Name : "Unknown";
                long size = (long)info.packedSize;

                typeBytes.TryGetValue(typeName, out long typeTotal);
                typeBytes[typeName] = typeTotal + size;

                if (!files.TryGetValue(assetPath, out FileEntry entry))
                {
                    entry = new FileEntry { path = assetPath, extension = GetExtension(assetPath) };
                    files.Add(assetPath, entry);
                }
                entry.bytes += size;
                // An asset can contain several objects (e.g. texture + sprites); show the biggest one's type
                if (size >= entry.largestPart)
                {
                    entry.largestPart = size;
                    entry.assetType = typeName;
                }
            }
        }

        data.packedTotal = files.Values.Sum(f => f.bytes);
        double total = Math.Max(1L, data.packedTotal);
        data.files = files.Values.OrderByDescending(f => f.bytes).ToList();
        foreach (FileEntry file in data.files)
        {
            file.percent = (float)(file.bytes * 100d / total);
        }
        data.categories = typeBytes
            .Select(pair => new CategoryEntry { name = pair.Key, bytes = pair.Value, percent = (float)(pair.Value * 100d / total) })
            .OrderByDescending(c => c.bytes)
            .ToList();
        return data;
    }

    // summary.totalSize is not the real apk/aab/ipa size, so read the output file from disk instead
    private static void ReadOutputFile(ReportData data)
    {
        string path = data.outputPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        data.outputFileSize = new FileInfo(path).Length;

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".apk" && extension != ".aab" && extension != ".ipa")
        {
            return;
        }

        try
        {
            Dictionary<string, OutputPart> parts = new Dictionary<string, OutputPart>();
            foreach (ZipEntryInfo entry in ReadZipEntries(path))
            {
                string name = GetOutputPartName(entry.name);
                if (!parts.TryGetValue(name, out OutputPart part))
                {
                    part = new OutputPart { name = name };
                    parts.Add(name, part);
                }
                part.compressed += entry.compressed;
                part.uncompressed += entry.uncompressed;
            }
            data.outputParts = parts.Values.OrderByDescending(p => p.compressed).ToList();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BuildReport] Could not read contents of " + path + ": " + e.Message);
        }
    }

    private static string GetOutputPartName(string entryPath)
    {
        // aab puts the app module under base/
        string path = entryPath.StartsWith("base/") ? entryPath.Substring(5) : entryPath;

        if (path.StartsWith("Payload/"))
        {
            if (path.Contains(".app/Data/")) return "Unity data (assets, scenes, resources)";
            if (path.Contains(".app/Frameworks/")) return "Frameworks (engine, plugins)";
            return "App bundle (other)";
        }
        if (path.StartsWith("assets/bin/Data/")) return "Unity data (assets, scenes, resources)";
        if (path.StartsWith("lib/"))
        {
            string[] parts = path.Split('/');
            return parts.Length > 2 ? "Native libs (" + parts[1] + ")" : "Native libs";
        }
        if (path.EndsWith(".dex") || path.StartsWith("dex/")) return "Java/Kotlin code (dex, SDKs)";
        if (path.StartsWith("res/") || path == "resources.arsc" || path == "resources.pb") return "Android resources";
        if (path.StartsWith("assets/")) return "Other assets (plugins)";
        if (path.StartsWith("META-INF/")) return "Signature / metadata";
        return "Other";
    }

    private struct ZipEntryInfo
    {
        public string name;
        public long compressed;
        public long uncompressed;
    }

    // Minimal zip central directory reader (apk/aab/ipa are zip files)
    private static List<ZipEntryInfo> ReadZipEntries(string path)
    {
        List<ZipEntryInfo> entries = new List<ZipEntryInfo>();
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            int tailLength = (int)Math.Min(stream.Length, 65557L);
            stream.Position = stream.Length - tailLength;
            byte[] tail = reader.ReadBytes(tailLength);
            int endRecord = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50)
                {
                    endRecord = i;
                    break;
                }
            }
            if (endRecord < 0)
            {
                throw new InvalidDataException("End of central directory not found");
            }

            int count = BitConverter.ToUInt16(tail, endRecord + 10);
            uint offset = BitConverter.ToUInt32(tail, endRecord + 16);
            if (count == 0xFFFF || offset == 0xFFFFFFFF)
            {
                throw new InvalidDataException("Zip64 files are not supported");
            }

            stream.Position = offset;
            for (int i = 0; i < count; i++)
            {
                if (reader.ReadUInt32() != 0x02014b50)
                {
                    throw new InvalidDataException("Invalid central directory entry");
                }
                stream.Position += 16;
                uint compressed = reader.ReadUInt32();
                uint uncompressed = reader.ReadUInt32();
                int nameLength = reader.ReadUInt16();
                int extraLength = reader.ReadUInt16();
                int commentLength = reader.ReadUInt16();
                stream.Position += 12;
                string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                stream.Position += extraLength + commentLength;
                entries.Add(new ZipEntryInfo { name = name, compressed = compressed, uncompressed = uncompressed });
            }
        }
        return entries;
    }

    private static string GetExtension(string path)
    {
        int slash = path.LastIndexOf('/');
        int dot = path.LastIndexOf('.');
        return dot > slash && dot < path.Length - 1 ? path.Substring(dot).ToLowerInvariant() : "(none)";
    }

    private static string GetFolderKey(string path, int depth)
    {
        string[] parts = path.Split('/');
        if (parts.Length <= 1)
        {
            return "(root)";
        }
        int count = Mathf.Min(depth, parts.Length - 1);
        return string.Join("/", parts, 0, count);
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        DrawToolbar();

        if (_report == null)
        {
            EditorGUILayout.HelpBox(_status ?? "No build report loaded.", MessageType.Info);
            return;
        }

        _tab = GUILayout.Toolbar(_tab, TabNames);
        switch (_tab)
        {
            case 0: DrawSummaryTab(); break;
            case 1: DrawFilesTab(); break;
            case 2: DrawGroupTab(); break;
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
        {
            Refresh();
        }
        if (GUILayout.Button("Show Report File", EditorStyles.toolbarButton, GUILayout.Width(110f)) && File.Exists(ReportPath))
        {
            EditorUtility.RevealInFinder(ReportPath);
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("Heavy threshold (KB)", EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck();
        float threshold = EditorGUILayout.DelayedFloatField(_thresholdKB, EditorStyles.toolbarTextField, GUILayout.Width(60f));
        if (EditorGUI.EndChangeCheck())
        {
            _thresholdKB = Mathf.Max(0f, threshold);
            EditorPrefs.SetFloat(ThresholdPrefKey, _thresholdKB);
            _filesDirty = true;
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSummaryTab()
    {
        _summaryScroll = EditorGUILayout.BeginScrollView(_summaryScroll);

        List<FileEntry> heavy = _report.files.Where(f => f.bytes >= ThresholdBytes).ToList();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last Build", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Platform", _report.platform);
        EditorGUILayout.LabelField("Result", _report.result);
        EditorGUILayout.LabelField("Finished at", _report.buildEndedAt);
        EditorGUILayout.LabelField("Build time", TimeSpan.FromTicks(_report.totalTimeTicks).ToString(@"hh\:mm\:ss"));
        EditorGUILayout.SelectableLabel("Output: " + _report.outputPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.LabelField("Output file size (on disk)",
            _report.outputFileSize >= 0 ? FormatBytes(_report.outputFileSize) : "(file not found)");
        EditorGUILayout.LabelField("Unity assets (uncompressed)", FormatBytes(_report.packedTotal));
        EditorGUILayout.LabelField("Asset count", _report.files.Count.ToString());
        EditorGUILayout.LabelField(
            $"Assets >= {_thresholdKB:0.#} KB",
            $"{heavy.Count} assets · {FormatBytes(heavy.Sum(f => f.bytes))}");

        if (_report.outputParts.Count > 0)
        {
            DrawOutputParts();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Size by Asset Type (uncompressed)", EditorStyles.boldLabel);
        foreach (CategoryEntry category in _report.categories)
        {
            Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            float x = row.x;
            GUI.Label(new Rect(x, row.y, 160f, row.height), category.name);
            x += 160f;
            GUI.Label(new Rect(x, row.y, 80f, row.height), FormatBytes(category.bytes));
            x += 80f;
            GUI.Label(new Rect(x, row.y, 55f, row.height), FormatPercent(category.percent));
            x += 60f;
            DrawBar(new Rect(x, row.y + 3f, Mathf.Max(0f, row.xMax - x - 4f), row.height - 6f), category.percent / 100f);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Top 20 Largest Assets", EditorStyles.boldLabel);
        int topCount = Mathf.Min(20, _report.files.Count);
        for (int i = 0; i < topCount; i++)
        {
            Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            DrawFileRow(row, i, _report.files[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawOutputParts()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output File Contents", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Asset sizes in this window are uncompressed. Inside the output file they are packed into " +
            "\"Unity data\" and compressed, so compare that row with the asset totals.",
            MessageType.None);

        Rect header = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
        GUI.Label(new Rect(header.x, header.y, 230f, header.height), "Part", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 230f, header.y, 90f, header.height), "In file", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 320f, header.y, 90f, header.height), "Uncompressed", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 410f, header.y, 55f, header.height), "%", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 465f, header.y, Mathf.Max(0f, header.width - 465f), header.height), string.Empty, EditorStyles.toolbarButton);

        double total = Math.Max(1L, _report.outputFileSize);
        for (int i = 0; i < _report.outputParts.Count; i++)
        {
            OutputPart part = _report.outputParts[i];
            float percent = (float)(part.compressed * 100d / total);
            Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            DrawRowBackground(row, i, false, false);
            GUI.Label(new Rect(row.x, row.y, 230f, row.height), part.name);
            GUI.Label(new Rect(row.x + 230f, row.y, 90f, row.height), FormatBytes(part.compressed));
            GUI.Label(new Rect(row.x + 320f, row.y, 90f, row.height), FormatBytes(part.uncompressed));
            GUI.Label(new Rect(row.x + 410f, row.y, 55f, row.height), FormatPercent(percent));
            DrawBar(new Rect(row.x + 469f, row.y + 3f, Mathf.Max(0f, row.xMax - row.x - 473f), row.height - 6f), percent / 100f);
        }
    }

    private void DrawFilesTab()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(150f));
        _typeIndex = EditorGUILayout.Popup(_typeIndex, _typeOptions, EditorStyles.toolbarPopup, GUILayout.Width(110f));
        _onlyHeavy = GUILayout.Toggle(_onlyHeavy, "Heavy only", EditorStyles.toolbarButton, GUILayout.Width(80f));
        if (EditorGUI.EndChangeCheck())
        {
            _filesDirty = true;
        }
        if (_filesDirty)
        {
            RebuildFileList();
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{_filteredFiles.Count} assets · {FormatBytes(_filteredBytes)}", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        Rect header = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
        float x = header.x;
        DrawSortHeader(new Rect(x, header.y, 80f, header.height), "Size", FileSort.Size);
        x += 80f;
        GUI.Label(new Rect(x, header.y, 55f, header.height), "%", EditorStyles.toolbarButton);
        x += 55f;
        DrawSortHeader(new Rect(x, header.y, 80f, header.height), "Ext", FileSort.Extension);
        x += 80f;
        DrawSortHeader(new Rect(x, header.y, 120f, header.height), "Asset Type", FileSort.AssetType);
        x += 120f;
        DrawSortHeader(new Rect(x, header.y, header.xMax - x - SelectButtonWidth, header.height), "Path", FileSort.Path);
        GUI.Label(new Rect(header.xMax - SelectButtonWidth, header.y, SelectButtonWidth, header.height), string.Empty, EditorStyles.toolbarButton);

        _filesScroll = DrawVirtualList(_filesScroll, _filteredFiles.Count, (row, i) => DrawFileRow(row, i, _filteredFiles[i]));
    }

    private void DrawSortHeader(Rect rect, string label, FileSort sort)
    {
        string arrow = _sort == sort ? (_sortAscending ? " ▲" : " ▼") : string.Empty;
        if (GUI.Button(rect, label + arrow, EditorStyles.toolbarButton))
        {
            if (_sort == sort)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sort = sort;
                _sortAscending = sort != FileSort.Size;
            }
            _filesDirty = true;
        }
    }

    private void RebuildFileList()
    {
        _filesDirty = false;
        IEnumerable<FileEntry> query = _report.files;
        if (!string.IsNullOrEmpty(_search))
        {
            query = query.Where(f => f.path.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                                     || f.assetType.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (_typeIndex > 0 && _typeIndex < _typeOptions.Length)
        {
            string type = _typeOptions[_typeIndex];
            query = query.Where(f => f.extension == type);
        }
        if (_onlyHeavy)
        {
            long threshold = ThresholdBytes;
            query = query.Where(f => f.bytes >= threshold);
        }

        switch (_sort)
        {
            case FileSort.Size:
                query = _sortAscending ? query.OrderBy(f => f.bytes) : query.OrderByDescending(f => f.bytes);
                break;
            case FileSort.Extension:
                query = _sortAscending
                    ? query.OrderBy(f => f.extension).ThenByDescending(f => f.bytes)
                    : query.OrderByDescending(f => f.extension).ThenByDescending(f => f.bytes);
                break;
            case FileSort.AssetType:
                query = _sortAscending
                    ? query.OrderBy(f => f.assetType).ThenByDescending(f => f.bytes)
                    : query.OrderByDescending(f => f.assetType).ThenByDescending(f => f.bytes);
                break;
            case FileSort.Path:
                query = _sortAscending
                    ? query.OrderBy(f => f.path, StringComparer.OrdinalIgnoreCase)
                    : query.OrderByDescending(f => f.path, StringComparer.OrdinalIgnoreCase);
                break;
        }

        _filteredFiles = query.ToList();
        _filteredBytes = _filteredFiles.Sum(f => f.bytes);
    }

    private void DrawFileRow(Rect row, int index, FileEntry file)
    {
        DrawRowBackground(row, index, file.bytes >= ThresholdBytes, file.path == _selectedPath);
        float x = row.x;
        GUI.Label(new Rect(x, row.y, 80f, row.height), FormatBytes(file.bytes));
        x += 80f;
        GUI.Label(new Rect(x, row.y, 55f, row.height), FormatPercent(file.percent));
        x += 55f;
        GUI.Label(new Rect(x, row.y, 80f, row.height), file.extension);
        x += 80f;
        GUI.Label(new Rect(x, row.y, 120f, row.height), file.assetType);
        x += 120f;
        GUI.Label(new Rect(x, row.y, row.xMax - x - SelectButtonWidth, row.height), file.path);
        DrawSelectButton(new Rect(row.xMax - SelectButtonWidth + 2f, row.y + 1f, SelectButtonWidth - 4f, row.height - 2f), file.path);
        HandleAssetRowClick(row, file.path);
    }

    private void DrawGroupTab()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        _groupMode = GUILayout.Toolbar(_groupMode, GroupModeNames, EditorStyles.toolbarButton, GUILayout.Width(300f));
        if (_groupMode == 0)
        {
            GUILayout.Label("Depth", EditorStyles.miniLabel, GUILayout.Width(36f));
            _folderDepth = EditorGUILayout.IntSlider(_folderDepth, 1, 6, GUILayout.Width(160f));
        }
        if (EditorGUI.EndChangeCheck())
        {
            _groupsDirty = true;
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("Click a group to list its assets", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        if (_groupsDirty)
        {
            RebuildGroups();
        }

        double total = Math.Max(1L, _report.packedTotal);
        long maxBytes = _groups.Count > 0 ? Math.Max(1L, _groups[0].bytes) : 1L;

        Rect header = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
        GUI.Label(new Rect(header.x, header.y, 80f, header.height), "Size", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 80f, header.y, 55f, header.height), "%", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 135f, header.y, 60f, header.height), "Assets", EditorStyles.toolbarButton);
        GUI.Label(new Rect(header.x + 195f, header.y, header.width - 195f, header.height), "Group", EditorStyles.toolbarButton);

        _groupScroll = DrawVirtualList(_groupScroll, _groups.Count, (row, i) =>
        {
            GroupRow group = _groups[i];
            DrawRowBackground(row, i, false, false);
            GUI.Label(new Rect(row.x, row.y, 80f, row.height), FormatBytes(group.bytes));
            GUI.Label(new Rect(row.x + 80f, row.y, 55f, row.height), FormatPercent((float)(group.bytes * 100d / total)));
            GUI.Label(new Rect(row.x + 135f, row.y, 60f, row.height), group.count.ToString());
            float barWidth = Mathf.Min(160f, row.width * 0.25f);
            GUI.Label(new Rect(row.x + 195f, row.y, row.width - 195f - barWidth - 8f, row.height), group.key);
            DrawBar(new Rect(row.xMax - barWidth - 4f, row.y + 3f, barWidth, row.height - 6f), (float)group.bytes / maxBytes);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                OpenGroupInFileList(group.key);
                e.Use();
            }
        });
    }

    private void RebuildGroups()
    {
        _groupsDirty = false;
        Func<FileEntry, string> keySelector;
        switch (_groupMode)
        {
            case 0: keySelector = f => GetFolderKey(f.path, _folderDepth); break;
            case 1: keySelector = f => f.extension; break;
            default: keySelector = f => f.assetType; break;
        }

        _groups = _report.files
            .GroupBy(keySelector)
            .Select(g => new GroupRow { key = g.Key, count = g.Count(), bytes = g.Sum(f => f.bytes) })
            .OrderByDescending(g => g.bytes)
            .ToList();
    }

    private void OpenGroupInFileList(string key)
    {
        _search = string.Empty;
        _typeIndex = 0;
        switch (_groupMode)
        {
            case 0:
                _search = key.StartsWith("(") ? string.Empty : key + "/";
                break;
            case 1:
                _typeIndex = Mathf.Max(0, Array.IndexOf(_typeOptions, key));
                break;
            default:
                _search = key;
                break;
        }
        _onlyHeavy = false;
        _filesDirty = true;
        _tab = 1;
        GUI.FocusControl(null);
    }

    #endregion

    #region Helpers

    private Vector2 DrawVirtualList(Vector2 scroll, int count, Action<Rect, int> drawRow)
    {
        float rowHeight = RowHeight;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        Rect area = GUILayoutUtility.GetRect(0f, Mathf.Max(1f, count * rowHeight), GUILayout.ExpandWidth(true));
        if (count > 0)
        {
            int first = Mathf.Clamp(Mathf.FloorToInt(scroll.y / rowHeight), 0, count - 1);
            int last = Mathf.Min(count, first + Mathf.CeilToInt(position.height / rowHeight) + 2);
            for (int i = first; i < last; i++)
            {
                drawRow(new Rect(area.x, area.y + i * rowHeight, area.width, rowHeight), i);
            }
        }
        EditorGUILayout.EndScrollView();
        return scroll;
    }

    private void DrawRowBackground(Rect row, int index, bool heavy, bool selected)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }
        if (selected)
        {
            EditorGUI.DrawRect(row, SelectedColor);
        }
        else if (heavy)
        {
            EditorGUI.DrawRect(row, HeavyColor);
        }
        else if (index % 2 == 1)
        {
            EditorGUI.DrawRect(row, StripeColor);
        }
    }

    private static void DrawBar(Rect rect, float fill)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }
        EditorGUI.DrawRect(rect, BarBackColor);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), BarFillColor);
    }

    // Drawn before HandleAssetRowClick so the button gets the mouse event first
    private void DrawSelectButton(Rect rect, string path)
    {
        bool inProject = path.StartsWith("Assets/") || path.StartsWith("Packages/");
        using (new EditorGUI.DisabledScope(!inProject))
        {
            if (GUI.Button(rect, "Select", EditorStyles.miniButton))
            {
                SelectInProject(path);
            }
        }
    }

    private void SelectInProject(string path)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null)
        {
            Debug.LogWarning("[BuildReport] Asset not found in project: " + path);
            return;
        }
        _selectedPath = path;
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    // Left click: ping asset, double click: select, right click: context menu
    private void HandleAssetRowClick(Rect row, string path)
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || !row.Contains(e.mousePosition))
        {
            return;
        }

        _selectedPath = path;
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (e.button == 0)
        {
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                if (e.clickCount == 2)
                {
                    SelectInProject(path);
                }
            }
        }
        else if (e.button == 1)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Path"), false, () => EditorGUIUtility.systemCopyBuffer = path);
            if (asset != null)
            {
                menu.AddItem(new GUIContent("Select in Project"), false, () => SelectInProject(path));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Select in Project"));
            }
            menu.ShowAsContext();
        }
        e.Use();
        Repaint();
    }

    private static string FormatBytes(long bytes)
    {
        return EditorUtility.FormatBytes(bytes);
    }

    private static string FormatPercent(float percent)
    {
        return percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    #endregion
}
}
