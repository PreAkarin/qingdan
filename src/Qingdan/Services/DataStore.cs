using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Qingdan.Models;

namespace Qingdan.Services;

public sealed class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _saveLock = new();

    public DataStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Qingdan");
        DataFilePath = Path.Combine(DataDirectory, "data.json");
        BackupFilePath = Path.Combine(DataDirectory, "data.backup.json");
    }

    public string DataDirectory { get; }

    public string DataFilePath { get; }

    public string BackupFilePath { get; }

    public LoadResult Load()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);

            if (!File.Exists(DataFilePath))
            {
                var first = AppData.CreateFirstRun();
                Save(first);
                return new LoadResult(first, null);
            }

            var json = File.ReadAllText(DataFilePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
            if (data is null)
            {
                throw new JsonException("数据文件内容为空。");
            }

            Normalize(data);
            return new LoadResult(data, null);
        }
        catch (Exception ex)
        {
            string? corruptPath = null;
            try
            {
                if (File.Exists(DataFilePath))
                {
                    corruptPath = Path.Combine(DataDirectory, $"data.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Copy(DataFilePath, corruptPath, overwrite: true);
                }
            }
            catch
            {
                // Best effort only.
            }

            var fallback = AppData.CreateFirstRun();
            try
            {
                Save(fallback);
            }
            catch
            {
                // The app remains usable in memory even if the disk is unavailable.
            }

            var message = corruptPath is null
                ? $"读取清单数据失败：{ex.Message}"
                : $"读取清单数据失败，已备份损坏文件到 {Path.GetFileName(corruptPath)}，并使用新的数据文件。";
            return new LoadResult(fallback, message);
        }
    }

    public void Save(AppData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_saveLock)
        {
            Directory.CreateDirectory(DataDirectory);

            var json = JsonSerializer.Serialize(data, JsonOptions);
            var tempPath = DataFilePath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            try
            {
                if (File.Exists(DataFilePath))
                {
                    File.Replace(tempPath, DataFilePath, BackupFilePath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, DataFilePath);
                }
            }
            catch (IOException)
            {
                // Some endpoint-protection tools briefly lock the target file. Retry a couple of
                // times before falling back to a plain overwrite.
                var saved = false;
                for (var attempt = 0; attempt < 3 && !saved; attempt++)
                {
                    Thread.Sleep(45);
                    try
                    {
                        File.Copy(tempPath, DataFilePath, overwrite: true);
                        File.Delete(tempPath);
                        saved = true;
                    }
                    catch (IOException)
                    {
                        // Retry.
                    }
                }

                if (!saved)
                {
                    throw;
                }
            }
        }
    }

    public void OpenDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = DataDirectory,
            UseShellExecute = true
        });
    }

    private static void Normalize(AppData data)
    {
        data.Tasks ??= new List<TodoItem>();
        data.Categories ??= new List<Category>();
        data.Settings ??= new AppSettings();

        var categoryIds = new HashSet<Guid>();
        foreach (var category in data.Categories)
        {
            if (category.Id == Guid.Empty || !categoryIds.Add(category.Id))
            {
                category.Id = Guid.NewGuid();
                categoryIds.Add(category.Id);
            }

            category.Name = string.IsNullOrWhiteSpace(category.Name) ? "未命名分类" : category.Name.Trim();
            category.Color = NormalizeColor(category.Color);
        }

        data.Categories = data.Categories
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToList();
        for (var index = 0; index < data.Categories.Count; index++)
        {
            data.Categories[index].SortOrder = index;
        }

        var taskIds = new HashSet<Guid>();
        foreach (var task in data.Tasks)
        {
            if (task.Id == Guid.Empty || !taskIds.Add(task.Id))
            {
                task.Id = Guid.NewGuid();
                taskIds.Add(task.Id);
            }

            task.Title = string.IsNullOrWhiteSpace(task.Title) ? "（无标题）" : task.Title.Trim();
            task.Notes ??= string.Empty;
            if (!Enum.IsDefined(task.Status))
            {
                task.Status = TodoStatus.Pending;
            }

            if (task.CreatedAt == default)
            {
                task.CreatedAt = DateTime.Now;
            }

            if (task.Status == TodoStatus.Done && task.CompletedAt is null)
            {
                task.CompletedAt = task.UpdatedAt == default ? task.CreatedAt : task.UpdatedAt;
            }
            else if (task.Status != TodoStatus.Done)
            {
                task.CompletedAt = null;
            }

            if (task.CategoryId is { } categoryId && !categoryIds.Contains(categoryId))
            {
                task.CategoryId = null;
            }

            task.UpdatedAt = task.UpdatedAt == default ? task.CreatedAt : task.UpdatedAt;
        }
    }

    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return "#5B7CFA";
        }

        var trimmed = color.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        if (trimmed.Length == 7 && trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return trimmed.ToUpperInvariant();
        }

        return "#5B7CFA";
    }
}

public sealed record LoadResult(AppData Data, string? Warning);
