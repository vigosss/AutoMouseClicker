using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 宏存储服务 - 负责宏配置的 JSON 读写
    /// </summary>
    public class MacroStorageService
    {
        private readonly string _dataDirectory;
        private readonly JsonSerializerOptions _jsonOptions;

        public MacroStorageService()
        {
            // 数据存储目录
            _dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            
            // 确保目录存在
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }

            // JSON 序列化选项
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            // 添加多态类型转换器
            _jsonOptions.Converters.Add(new MacroActionConverter());
        }

        /// <summary>
        /// 保存宏配置到文件（原子写入，防止崩溃导致数据损坏）
        /// </summary>
        /// <param name="profile">宏配置</param>
        /// <returns>保存的文件路径</returns>
        public string Save(MacroProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            // 更新修改时间
            profile.UpdatedAt = DateTime.Now;

            // 生成文件名（使用名称的合法版本）
            var fileName = GetSafeFileName(profile.Name, profile.Id);
            var filePath = Path.Combine(_dataDirectory, fileName);

            // 清理该 ID 的旧文件（处理重命名场景：名称变了，旧文件名不同）
            CleanupOldFiles(profile.Id, fileName);

            // 序列化
            var json = JsonSerializer.Serialize(profile, _jsonOptions);

            // 原子写入：先写入临时文件，再替换目标文件
            var tempFile = filePath + ".tmp";
            File.WriteAllText(tempFile, json);

            try
            {
                File.Move(tempFile, filePath, overwrite: true);
            }
            catch
            {
                // 如果 Move 失败（例如跨分区），回退到直接写入
                try { File.Delete(tempFile); } catch { /* 忽略清理失败 */ }
                File.WriteAllText(filePath, json);
            }

            return filePath;
        }

        /// <summary>
        /// 清理该宏 ID 的旧文件（文件名不匹配当前名称的）
        /// </summary>
        private void CleanupOldFiles(string profileId, string currentFileName)
        {
            try
            {
                var files = Directory.GetFiles(_dataDirectory, $"*{profileId}.json");
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (name != currentFileName)
                    {
                        // 文件名不一致，说明宏被重命名了，删除旧文件
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // 清理失败不影响保存流程
            }
        }

        /// <summary>
        /// 从文件加载宏配置
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>宏配置</returns>
        public MacroProfile Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"宏文件不存在: {filePath}");

            var json = File.ReadAllText(filePath);
            var profile = JsonSerializer.Deserialize<MacroProfile>(json, _jsonOptions);
            
            if (profile == null)
                throw new InvalidOperationException($"无法解析宏文件: {filePath}");

            return profile;
        }

        /// <summary>
        /// 根据 ID 加载宏配置
        /// </summary>
        /// <param name="id">宏 ID</param>
        /// <returns>宏配置，如果不存在返回 null</returns>
        public MacroProfile? LoadById(string id)
        {
            var files = Directory.GetFiles(_dataDirectory, "*.json");
            
            foreach (var file in files)
            {
                try
                {
                    var profile = Load(file);
                    if (profile.Id == id)
                        return profile;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"加载宏文件失败，已跳过: {file}, 错误: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// 获取所有保存的宏配置
        /// </summary>
        /// <returns>宏配置列表</returns>
        public List<MacroProfile> LoadAll()
        {
            var profiles = new List<MacroProfile>();
            
            if (!Directory.Exists(_dataDirectory))
                return profiles;

            var files = Directory.GetFiles(_dataDirectory, "*.json");
            
            foreach (var file in files)
            {
                try
                {
                    var profile = Load(file);
                    profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"加载宏文件失败，已跳过: {file}, 错误: {ex.Message}");
                }
            }

            // 按修改时间排序（最新的在前）
            profiles.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            
            return profiles;
        }

        /// <summary>
        /// 删除宏配置
        /// </summary>
        /// <param name="id">宏 ID</param>
        /// <returns>是否删除成功</returns>
        public bool Delete(string id)
        {
            var files = Directory.GetFiles(_dataDirectory, "*.json");
            
            foreach (var file in files)
            {
                try
                {
                    var profile = Load(file);
                    if (profile.Id == id)
                    {
                        File.Delete(file);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"删除宏文件失败，已跳过: {file}, 错误: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// 批量保存宏配置（每个宏单独保存为一个文件）
        /// </summary>
        /// <param name="profiles">宏配置列表</param>
        public void SaveMacros(IEnumerable<MacroProfile> profiles)
        {
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));

            foreach (var profile in profiles)
            {
                Save(profile);
            }
        }

        /// <summary>
        /// 加载所有宏配置（LoadAll 的别名，语义更清晰）
        /// </summary>
        /// <returns>宏配置列表</returns>
        public List<MacroProfile> LoadMacros()
        {
            return LoadAll();
        }

        /// <summary>
        /// 检查宏名称是否已存在
        /// </summary>
        /// <param name="name">宏名称</param>
        /// <param name="excludeId">排除的 ID（用于编辑时检查）</param>
        /// <returns>是否存在同名宏</returns>
        public bool NameExists(string name, string? excludeId = null)
        {
            var profiles = LoadAll();
            return profiles.Exists(p => p.Name == name && p.Id != excludeId);
        }

        /// <summary>
        /// 生成合法的文件名
        /// </summary>
        private string GetSafeFileName(string name, string id)
        {
            // 移除非法字符
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            
            // 限制长度
            if (safeName.Length > 50)
                safeName = safeName.Substring(0, 50);
            
            // 添加 ID 后缀确保唯一性
            return $"{safeName}_{id}.json";
        }
    }

    /// <summary>
    /// MacroAction 多态 JSON 转换器
    /// </summary>
    public class MacroActionConverter : JsonConverter<MacroAction>
    {
        public override MacroAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            
            // 读取类型字段
            if (!root.TryGetProperty("Type", out var typeProp))
                throw new JsonException("缺少 Type 属性");

            var typeString = typeProp.GetString();
            
            // 根据类型反序列化（忽略大小写，提高容错性）
            if (string.Equals(typeString, "FindImage", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<FindImageAction>(root.GetRawText(), options)!;
            if (string.Equals(typeString, "Wait", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<WaitAction>(root.GetRawText(), options)!;
            if (string.Equals(typeString, "MouseClick", StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.Deserialize<MouseClickAction>(root.GetRawText(), options)!;

            throw new JsonException($"未知的动作类型: {typeString}");
        }

        public override void Write(Utf8JsonWriter writer, MacroAction value, JsonSerializerOptions options)
        {
            // 根据实际类型序列化
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}