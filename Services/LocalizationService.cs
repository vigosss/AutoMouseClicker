using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using Ming_AutoClicker.Models;

namespace Ming_AutoClicker.Services
{
    /// <summary>
    /// 管理应用语言资源，并为代码侧消息提供统一的字符串入口。
    /// </summary>
    public sealed class LocalizationService
    {
        private const string EnglishDictionary = "Localization/Strings.en-US.xaml";
        private const string ChineseDictionary = "Localization/Strings.zh-CN.xaml";
        private static readonly AppLanguage SystemLanguage =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
                "zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.SimplifiedChinese
                : AppLanguage.English;

        public static LocalizationService Current { get; } = new LocalizationService();

        private readonly ResourceDictionary _englishFallback = new()
        {
            Source = new Uri(EnglishDictionary, UriKind.Relative)
        };

        private LocalizationService()
        {
        }

        public AppLanguage SelectedLanguage { get; private set; } = AppLanguage.System;

        public AppLanguage EffectiveLanguage { get; private set; } = AppLanguage.English;

        public event EventHandler? LanguageChanged;

        public void ApplyLanguage(AppLanguage selectedLanguage)
        {
            if (!Enum.IsDefined(selectedLanguage))
                selectedLanguage = AppLanguage.System;

            var effectiveLanguage = ResolveEffectiveLanguage(selectedLanguage);
            SelectedLanguage = selectedLanguage;
            EffectiveLanguage = effectiveLanguage;

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            foreach (var dictionary in dictionaries
                         .Where(d => IsLocalizationDictionary(d.Source))
                         .ToList())
            {
                dictionaries.Remove(dictionary);
            }

            // 英文资源始终作为兜底；中文资源放在后面覆盖同名 key。
            dictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri(EnglishDictionary, UriKind.Relative)
            });
            if (effectiveLanguage == AppLanguage.SimplifiedChinese)
            {
                dictionaries.Insert(1, new ResourceDictionary
                {
                    Source = new Uri(ChineseDictionary, UriKind.Relative)
                });
            }

            CultureInfo.CurrentUICulture = effectiveLanguage == AppLanguage.SimplifiedChinese
                ? CultureInfo.GetCultureInfo("zh-CN")
                : CultureInfo.GetCultureInfo("en-US");

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key)
        {
            if (Application.Current?.TryFindResource(key) is string value)
                return value;
            if (_englishFallback[key] is string fallback)
                return fallback;
            return key;
        }

        public string Format(string key, params object?[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, GetString(key), args);
        }

        public string GetMatchFailureText(MatchFailureReason reason)
        {
            return GetString(reason switch
            {
                MatchFailureReason.InvalidTemplate => "MatchInvalidTemplate",
                MatchFailureReason.CaptureFailed => "MatchCaptureFailed",
                MatchFailureReason.MatchingError => "MatchCalculationFailed",
                MatchFailureReason.Cancelled => "MatchCancelled",
                MatchFailureReason.TimedOut => "MatchTimedOut",
                _ => "MatchNoCandidate"
            });
        }

        public static AppLanguage ResolveEffectiveLanguage(AppLanguage selectedLanguage)
        {
            if (selectedLanguage != AppLanguage.System)
                return selectedLanguage;

            return SystemLanguage;
        }

        private static bool IsLocalizationDictionary(Uri? source)
        {
            if (source == null)
                return false;

            var path = source.OriginalString.Replace('\\', '/');
            return path.EndsWith(EnglishDictionary, StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(ChineseDictionary, StringComparison.OrdinalIgnoreCase);
        }
    }
}
