using System.Globalization;

namespace Kadr.Core
{
    /// <summary>
    /// Two-language UI: every user-facing string is written inline as <c>L.T("русский", "English")</c>.
    /// The language follows Windows (Russian for ru/uk/be/kk, English otherwise) until picked in the settings.
    /// </summary>
    public static class L
    {
        public static bool En { get; private set; }

        public static string AppName => En ? "Kadr" : "Кадр";

        public static string T(string ru, string en) => En ? en : ru;

        /// <summary>Settings.Language: null — follow Windows, "ru" or "en" — picked by the user.</summary>
        public static void Apply(string language) => En = (language ?? SystemLanguage()) == "en";

        public static string SystemLanguage() =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? "ru" : "en";
    }
}
