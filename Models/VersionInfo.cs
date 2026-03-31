using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ming_AutoClicker.Models
{
    /// <summary>
    /// GitHub Release 信息模型
    /// 用于解析 GitHub Releases API 返回的 JSON 数据
    /// </summary>
    public class GitHubRelease
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool PreRelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    /// <summary>
    /// GitHub Release Asset 模型
    /// </summary>
    public class GitHubAsset
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("content_type")]
        public string ContentType { get; set; } = string.Empty;
    }

    /// <summary>
    /// 版本检查结果
    /// </summary>
    public class UpdateCheckResult
    {
        /// <summary>
        /// 是否有新版本可用
        /// </summary>
        public bool HasUpdate { get; set; }

        /// <summary>
        /// 当前版本
        /// </summary>
        public Version CurrentVersion { get; set; } = new(0, 0, 0);

        /// <summary>
        /// 最新版本
        /// </summary>
        public Version LatestVersion { get; set; } = new(0, 0, 0);

        /// <summary>
        /// 最新版本号字符串（如 "v0.2.0"）
        /// </summary>
        public string LatestVersionTag { get; set; } = string.Empty;

        /// <summary>
        /// 更新说明（Markdown 格式）
        /// </summary>
        public string ReleaseNotes { get; set; } = string.Empty;

        /// <summary>
        /// 发布时间
        /// </summary>
        public DateTime PublishedAt { get; set; }

        /// <summary>
        /// 下载 URL（zip 包）
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// 下载文件大小（字节）
        /// </summary>
        public long DownloadSize { get; set; }

        /// <summary>
        /// Release 页面 URL（备用）
        /// </summary>
        public string ReleasePageUrl { get; set; } = string.Empty;

        /// <summary>
        /// 错误信息（检查失败时）
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}