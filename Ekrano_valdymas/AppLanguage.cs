using System;

namespace Ekrano_valdymas
{
    internal enum AppLanguage
    {
        English,
        Lithuanian
    }

    internal static class LanguageManager
    {
        private static AppLanguage _current = AppLanguage.English;

        public static event EventHandler? Changed;

        public static AppLanguage Current
        {
            get => _current;
            set
            {
                if (_current == value)
                    return;

                _current = value;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool IsLithuanian => Current == AppLanguage.Lithuanian;

        public static string Text(string english, string lithuanian) =>
            IsLithuanian ? lithuanian : english;
    }
}
