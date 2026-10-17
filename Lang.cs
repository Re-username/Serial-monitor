namespace SerialPlotter;

// Двуязычие: русский (по умолчанию) / English.
// Выбор хранится в настройках, переключатель — в меню «Инструменты».
// Все пользовательские строки проходят через Lang.T(ru, en).
public static class Lang
{
    public static bool English;

    public static string T(string ru, string en) => English ? en : ru;
}
