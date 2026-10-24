using System.Globalization;

namespace Skimmer;

/// <summary>Tiny localization table: Russian and English.</summary>
static class L
{
    static readonly bool ru;

    static L()
    {
        string lang = AppSettings.Current.Language ?? "auto";
        if (lang == "auto") lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        ru = lang is "ru" or "uk" or "be" or "kk";
    }

    public static string T(string key)
        => S.TryGetValue(key, out var v) ? (ru ? v.ru : v.en) : key;

    public static string F(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, T(key), args);

    static readonly Dictionary<string, (string ru, string en)> S = new()
    {
        ["app.title"] = ("Skimmer", "Skimmer"),

        ["tree.indexed"] = ("★ Индексируемые папки", "★ Indexed folders"),
        ["tree.desktop"] = ("Рабочий стол", "Desktop"),
        ["tree.documents"] = ("Документы", "Documents"),
        ["tree.pictures"] = ("Изображения", "Pictures"),
        ["tree.downloads"] = ("Загрузки", "Downloads"),
        ["tree.network"] = ("сеть", "network"),

        ["tip.back"] = ("Назад (Alt+←)", "Back (Alt+Left)"),
        ["tip.forward"] = ("Вперёд (Alt+→)", "Forward (Alt+Right)"),
        ["tip.up"] = ("Вверх (Alt+↑)", "Up (Alt+Up)"),
        ["tip.refresh"] = ("Обновить (F5)", "Refresh (F5)"),
        ["tip.settings"] = ("Настройки", "Settings"),
        ["tip.desc"] = ("Порядок сортировки", "Sort order"),
        ["tip.size"] = ("Размер превью", "Thumbnail size"),

        ["sort.name"] = ("По имени", "By name"),
        ["sort.date"] = ("По дате", "By date"),
        ["sort.size"] = ("По размеру", "By size"),
        ["chk.names"] = ("Имена", "Names"),

        ["status.loading"] = ("Чтение папки…", "Reading folder…"),
        ["status.files"] = ("Файлов: {0} · {1}", "Files: {0} · {1}"),
        ["status.index"] = ("Миниатюры: {0} / {1}", "Thumbnails: {0} / {1}"),
        ["status.ready"] = ("Все превью готовы", "All thumbnails ready"),
        ["status.bg"] = ("Фоновая индексация: {0}", "Background indexing: {0}"),
        ["status.error"] = ("Не удалось открыть папку: {0}", "Cannot open folder: {0}"),
        ["grid.empty"] = ("В этой папке нет изображений", "No images in this folder"),
        ["grid.broken"] = ("?", "?"),

        ["ctx.open"] = ("Открыть", "Open"),
        ["ctx.explorer"] = ("Показать в проводнике", "Show in Explorer"),
        ["ctx.copypath"] = ("Копировать путь", "Copy path"),
        ["ctx.copyfile"] = ("Копировать файл", "Copy file"),
        ["ctx.refresh"] = ("Обновить папку", "Refresh folder"),
        ["ctx.delete"] = ("Удалить в корзину", "Delete to Recycle Bin"),
        ["status.selected"] = ("Выбрано: {0}", "Selected: {0}"),

        ["upd.available"] = ("⬆ Доступна версия {0} — обновить", "⬆ Version {0} is available — update"),
        ["upd.title"] = ("Обновление Skimmer", "Skimmer update"),
        ["upd.header"] = ("Доступна версия {0} (у вас {1})", "Version {0} is available (you have {1})"),
        ["upd.install"] = ("Обновить и перезапустить", "Update and restart"),
        ["upd.open"] = ("Открыть страницу загрузки", "Open download page"),
        ["upd.later"] = ("Позже", "Later"),
        ["upd.skip"] = ("Пропустить эту версию", "Skip this version"),
        ["upd.manual"] = ("Папка программы недоступна для записи, поэтому обновить её автоматически нельзя. Скачайте новую версию со страницы релиза.",
                          "The program folder is read-only, so it cannot update itself. Download the new version from the release page."),
        ["upd.downloading"] = ("Загрузка… {0}%", "Downloading… {0}%"),
        ["upd.failed"] = ("Не удалось обновить: {0}", "Update failed: {0}"),
        ["upd.uptodate"] = ("У вас последняя версия ({0}).", "You have the latest version ({0})."),
        ["upd.checkfail"] = ("Не удалось проверить обновления: {0}", "Could not check for updates: {0}"),
        ["set.upd"] = ("Обновления", "Updates"),
        ["set.upd.auto"] = ("Проверять обновления на GitHub автоматически", "Check GitHub for updates automatically"),
        ["set.upd.version"] = ("Текущая версия: {0}", "Current version: {0}"),
        ["set.upd.check"] = ("Проверить сейчас", "Check now"),

        ["err.nofolder"] = ("Папка не найдена", "Folder not found"),

        ["set.title"] = ("Настройки Skimmer", "Skimmer settings"),
        ["set.bg"] = ("Фоновая индексация", "Background indexing"),
        ["set.bg.enable"] = ("Индексировать эти папки в фоне, пока программа запущена",
                             "Index these folders in the background while the app is running"),
        ["set.add"] = ("Добавить…", "Add…"),
        ["set.remove"] = ("Удалить", "Remove"),
        ["set.sub"] = ("Включая вложенные папки", "Include subfolders"),
        ["set.rescan"] = ("Повторная проверка, мин:", "Re-scan every (min):"),
        ["set.cache"] = ("Кэш миниатюр", "Thumbnail cache"),
        ["set.cache.path"] = ("Расположение: {0}", "Location: {0}"),
        ["set.cache.used"] = ("Занято: {0}", "In use: {0}"),
        ["set.cache.age"] = ("Удалять кэш папок, не открывавшихся дней:", "Delete cache of folders unused for (days):"),
        ["set.cache.size"] = ("Максимальный размер кэша, МБ:", "Maximum cache size (MB):"),
        ["set.cache.open"] = ("Открыть папку кэша", "Open cache folder"),
        ["set.cache.clear"] = ("Очистить кэш сейчас", "Clear cache now"),
        ["set.cache.cleared"] = ("Удалено файлов кэша: {0} ({1})", "Cache files deleted: {0} ({1})"),
        ["set.theme"] = ("Тема оформления:", "Theme:"),
        ["theme.dark"] = ("Тёмная", "Dark"),
        ["theme.light"] = ("Светлая", "Light"),
        ["theme.system"] = ("Как в Windows", "Same as Windows"),
        ["set.restart"] = ("Тема и язык применяются после перезапуска. Перезапустить Skimmer сейчас?",
                           "Theme and language are applied after a restart. Restart Skimmer now?"),
        ["set.lang"] = ("Язык интерфейса:", "Interface language:"),
        ["set.lang.auto"] = ("Авто", "Auto"),
        ["set.folderdlg"] = ("Выберите папку для фоновой индексации", "Choose a folder to index in the background"),
        ["ok"] = ("OK", "OK"),
        ["cancel"] = ("Отмена", "Cancel"),

        ["viewer.error"] = ("Не удалось открыть изображение", "Cannot open this image"),
    };
}
