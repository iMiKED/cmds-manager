using System;
using System.Collections.Generic;
using CmdsManager.Domain;

namespace CmdsManager.Infrastructure.Configuration
{
    internal static class LocalizationDefaults
    {
        internal static LocalizationSettings Create()
        {
            var result = new LocalizationSettings { Language = "ru" };
            result.Languages["ru"] = BuildRussian();
            result.Languages["en"] = BuildEnglish();
            return result;
        }

        private static Dictionary<string, string> BuildRussian()
        {
            return Dictionary(new[]
            {
                Pair("Language.Name", "Русский"), Pair("Common.Yes", "Да"), Pair("Common.No", "Нет"),
                Pair("Common.Save", "Сохранить"), Pair("Common.Cancel", "Отмена"), Pair("Common.Browse", "Обзор…"),
                Pair("Common.Close", "Закрыть"), Pair("Main.Add", "Добавить"), Pair("Main.Edit", "Изменить"),
                Pair("Main.Delete", "Удалить"), Pair("Main.Start", "Запустить"), Pair("Main.Stop", "Остановить"),
                Pair("Main.StartAll", "Запустить всё"), Pair("Main.StopAll", "Остановить всё"),
                Pair("Main.Reload", "Перечитать INI"), Pair("Main.Settings", "Настройки"),
                Pair("Main.About", "О программе"), Pair("Main.Exit", "Выход"), Pair("Main.Filter", "Фильтр:"),
                Pair("Main.FilterHint", "Фильтр по имени, пути и типу"), Pair("Main.Column.Name", "Название"),
                Pair("Main.Column.Type", "Тип"), Pair("Main.Column.Interpreter", "Интерпретатор"),
                Pair("Main.Column.AutoStart", "Авто"), Pair("Main.Column.State", "Состояние"),
                Pair("Main.Column.Started", "Запущен"), Pair("Main.Column.ExitCode", "Код"), Pair("Main.Column.Path", "Путь"),
                Pair("Main.Context.EditEntry", "Изменить запись"), Pair("Main.Context.EditFile", "Редактировать файл"),
                Pair("Main.Context.ShowFolder", "Показать в папке"), Pair("Main.Context.DeleteEntry", "Удалить запись"),
                Pair("Main.State.Starting", "Запуск…"), Pair("Main.State.Running", "Работает"),
                Pair("Main.State.RunningMany", "Работает ({0})"), Pair("Main.State.Stopping", "Остановка…"),
                Pair("Main.State.Exited", "Завершён"), Pair("Main.State.Failed", "Ошибка"), Pair("Main.State.Stopped", "Остановлен"),
                Pair("Main.RunTitle", "Запуск скриптов"), Pair("Main.DeleteTitle", "Удаление записи"),
                Pair("Main.DeleteRunning", "Сначала остановите скрипт, затем удалите запись."),
                Pair("Main.DeleteConfirm", "Удалить запись «{0}»?\\n\\nСам файл скрипта удалён не будет."),
                Pair("Main.Disabled", "Запись отключена. Включите её в редакторе перед запуском."),
                Pair("Main.StartFailed", "Не удалось запустить «{0}»."), Pair("Main.StopFailed", "Не удалось остановить «{0}»."),
                Pair("Main.StopAllFailed", "Не удалось остановить все скрипты."), Pair("Main.EditorFailed", "Не удалось открыть редактор."),
                Pair("Main.FolderFailed", "Не удалось открыть папку."), Pair("Main.SettingsSaved", "Настройки сохранены. Срок хранения журналов применяется после перезапуска."),
                Pair("Main.SettingsSaveFailed", "Не удалось сохранить настройки."), Pair("Main.ReloadRunning", "Перед перечитыванием INI остановите все скрипты."),
                Pair("Main.ReloadFailed", "Не удалось перечитать INI."), Pair("Main.SaveFailed", "Не удалось сохранить INI."),
                Pair("Main.ScriptNotFound", "Скрипт «{0}» не найден в конфигурации."),
                Pair("Console.Empty", "Вывод появится здесь после запуска скрипта"), Pair("Console.Running", "работает"),
                Pair("Console.Exited", "код {0}"), Pair("Console.Clear", "Очистить"), Pair("Console.CloseTab", "Закрыть вкладку"),
                Pair("Script.Title.Add", "Добавление скрипта"), Pair("Script.Title.Edit", "Редактирование скрипта"),
                Pair("Script.Tab.General", "Основное"), Pair("Script.Tab.Launch", "Запуск"), Pair("Script.Name", "Название"),
                Pair("Script.File", "Файл"), Pair("Script.Enabled", "Запись активна"), Pair("Script.Interpreter", "Интерпретатор"),
                Pair("Script.Arguments", "Аргументы"), Pair("Script.WorkingDirectory", "Рабочая папка"), Pair("Script.WindowMode", "Режим окна"),
                Pair("Script.Capture", "Перехватывать stdout/stderr"), Pair("Script.Encoding", "Кодировка вывода"),
                Pair("Script.Parallel", "Разрешить параллельные экземпляры"), Pair("Script.AutoStart", "Запускать при старте CmdsManager"),
                Pair("Script.AutoStartOrder", "Порядок автозапуска"), Pair("Script.AutoStartDelay", "Задержка, с"),
                Pair("Script.StopPolicy", "Остановка"), Pair("Script.StopTimeout", "Таймаут, с"),
                Pair("Script.Note", "Повышение прав не поддерживается. Удаление записи не удаляет файл."),
                Pair("Script.Window.Hidden", "Скрыто"), Pair("Script.Window.Normal", "Обычное окно"), Pair("Script.Window.Minimized", "Свёрнуто"),
                Pair("Script.Stop.Graceful", "Корректно, затем принудительно"), Pair("Script.Stop.Kill", "Сразу принудительно"),
                Pair("Script.Interpreter.Auto", "Автоматически"), Pair("Script.Interpreter.VbsConsole", "VBS — cscript.exe"),
                Pair("Script.Interpreter.VbsWindow", "VBS — wscript.exe"), Pair("Script.Encoding.Auto", "Авто (OEM Windows)"),
                Pair("Script.Encoding.Utf8", "UTF-8"), Pair("Script.Encoding.Oem", "OEM Windows"),
                Pair("Script.Encoding.Windows1251", "Windows-1251"), Pair("Script.Encoding.Utf16", "UTF-16 LE"),
                Pair("Script.ValidationTitle", "Проверка записи"), Pair("Script.FileMissing", "Файл скрипта не найден."),
                Pair("Script.DirectoryMissing", "Рабочая папка не найдена."), Pair("Script.FileFilter", "Поддерживаемые скрипты|*.cmd;*.bat;*.ps1;*.vbs|Все файлы|*.*"),
                Pair("Script.SelectFile", "Выберите скрипт"), Pair("Script.SelectDirectory", "Выберите рабочую папку"),
                Pair("Settings.Title", "Настройки CmdsManager"), Pair("Settings.Tab.General", "Основное"), Pair("Settings.Tab.Tools", "Пути и журналы"),
                Pair("Settings.StartWithWindows", "Запускать CmdsManager при входе в Windows"), Pair("Settings.StartMinimized", "При ручном старте скрывать в трей"),
                Pair("Settings.AutoStartScripts", "Запускать отмеченные скрипты при старте"), Pair("Settings.ConfirmDelete", "Подтверждать удаление записи"),
                Pair("Settings.Language", "Язык"), Pair("Settings.ConsoleFont", "Шрифт консоли"), Pair("Settings.ChooseFont", "Выбрать…"),
                Pair("Settings.Editor", "Редактор"), Pair("Settings.EditorArguments", "Аргументы редактора"), Pair("Settings.PowerShell7", "Путь к pwsh.exe"),
                Pair("Settings.Retention", "Хранить журналы, дней"), Pair("Settings.LogOutput", "Записывать stdout/stderr в журнал (может содержать секреты)"),
                Pair("Settings.Warning", "Автозапуск действует для текущего пользователя. После его включения не перемещайте portable-папку."),
                Pair("Settings.ValidationTitle", "Проверка настроек"), Pair("Settings.EditorRequired", "Укажите путь к редактору."),
                Pair("Settings.EditorMissing", "Редактор не найден."), Pair("Settings.PowerShellMissing", "Указанный путь PowerShell 7 не найден."),
                Pair("Settings.AppFilter", "Приложения|*.exe|Все файлы|*.*"), Pair("Settings.PowerShellFilter", "PowerShell 7|pwsh.exe|Приложения|*.exe"),
                Pair("About.Title", "О программе"), Pair("About.Description", "Менеджер CMD, BAT, PowerShell и VBS-скриптов"), Pair("About.Version", "Версия {0}"),
                Pair("Tray.Toggle", "Открыть / скрыть"), Pair("Tray.AutoStartFailed", "Не удалось автоматически запустить скриптов: {0}. Откройте приложение для подробностей."),
                Pair("Tray.ExitTitle", "Выход из CmdsManager"), Pair("Tray.ExitConfirm", "Все запущенные через CmdsManager скрипты будут остановлены. Выйти?"),
                Pair("Tray.Exiting", "CmdsManager — завершение"), Pair("App.UiErrorTitle", "Ошибка CmdsManager")
            });
        }

        private static Dictionary<string, string> BuildEnglish()
        {
            return Dictionary(new[]
            {
                Pair("Language.Name", "English"), Pair("Common.Yes", "Yes"), Pair("Common.No", "No"),
                Pair("Common.Save", "Save"), Pair("Common.Cancel", "Cancel"), Pair("Common.Browse", "Browse…"), Pair("Common.Close", "Close"),
                Pair("Main.Add", "Add"), Pair("Main.Edit", "Edit"), Pair("Main.Delete", "Delete"), Pair("Main.Start", "Start"), Pair("Main.Stop", "Stop"),
                Pair("Main.StartAll", "Start all"), Pair("Main.StopAll", "Stop all"), Pair("Main.Reload", "Reload INI"), Pair("Main.Settings", "Settings"),
                Pair("Main.About", "About"), Pair("Main.Exit", "Exit"), Pair("Main.Filter", "Filter:"), Pair("Main.FilterHint", "Filter by name, path, and type"),
                Pair("Main.Column.Name", "Name"), Pair("Main.Column.Type", "Type"), Pair("Main.Column.Interpreter", "Interpreter"), Pair("Main.Column.AutoStart", "Auto"),
                Pair("Main.Column.State", "State"), Pair("Main.Column.Started", "Started"), Pair("Main.Column.ExitCode", "Code"), Pair("Main.Column.Path", "Path"),
                Pair("Main.Context.EditEntry", "Edit entry"), Pair("Main.Context.EditFile", "Edit file"), Pair("Main.Context.ShowFolder", "Show in folder"), Pair("Main.Context.DeleteEntry", "Delete entry"),
                Pair("Main.State.Starting", "Starting…"), Pair("Main.State.Running", "Running"), Pair("Main.State.RunningMany", "Running ({0})"), Pair("Main.State.Stopping", "Stopping…"),
                Pair("Main.State.Exited", "Exited"), Pair("Main.State.Failed", "Failed"), Pair("Main.State.Stopped", "Stopped"), Pair("Main.RunTitle", "Start scripts"),
                Pair("Main.DeleteTitle", "Delete entry"), Pair("Main.DeleteRunning", "Stop the script before deleting its entry."),
                Pair("Main.DeleteConfirm", "Delete “{0}”?\\n\\nThe script file will not be deleted."), Pair("Main.Disabled", "This entry is disabled. Enable it before starting."),
                Pair("Main.StartFailed", "Could not start “{0}”."), Pair("Main.StopFailed", "Could not stop “{0}”."), Pair("Main.StopAllFailed", "Could not stop all scripts."),
                Pair("Main.EditorFailed", "Could not open the editor."), Pair("Main.FolderFailed", "Could not open the folder."),
                Pair("Main.SettingsSaved", "Settings saved. Log retention changes apply after restart."), Pair("Main.SettingsSaveFailed", "Could not save settings."),
                Pair("Main.ReloadRunning", "Stop all scripts before reloading the INI file."), Pair("Main.ReloadFailed", "Could not reload the INI file."), Pair("Main.SaveFailed", "Could not save the INI file."),
                Pair("Main.ScriptNotFound", "Script “{0}” was not found in the configuration."),
                Pair("Console.Empty", "Script output will appear here"), Pair("Console.Running", "running"), Pair("Console.Exited", "code {0}"),
                Pair("Console.Clear", "Clear"), Pair("Console.CloseTab", "Close tab"), Pair("Script.Title.Add", "Add script"), Pair("Script.Title.Edit", "Edit script"),
                Pair("Script.Tab.General", "General"), Pair("Script.Tab.Launch", "Launch"), Pair("Script.Name", "Name"), Pair("Script.File", "File"), Pair("Script.Enabled", "Entry is enabled"),
                Pair("Script.Interpreter", "Interpreter"), Pair("Script.Arguments", "Arguments"), Pair("Script.WorkingDirectory", "Working directory"), Pair("Script.WindowMode", "Window mode"),
                Pair("Script.Capture", "Capture stdout/stderr"), Pair("Script.Encoding", "Output encoding"), Pair("Script.Parallel", "Allow parallel instances"),
                Pair("Script.AutoStart", "Start with CmdsManager"), Pair("Script.AutoStartOrder", "Auto-start order"), Pair("Script.AutoStartDelay", "Delay, seconds"),
                Pair("Script.StopPolicy", "Stop policy"), Pair("Script.StopTimeout", "Timeout, seconds"), Pair("Script.Note", "Elevation is not supported. Deleting an entry does not delete its file."),
                Pair("Script.Window.Hidden", "Hidden"), Pair("Script.Window.Normal", "Normal"), Pair("Script.Window.Minimized", "Minimized"),
                Pair("Script.Stop.Graceful", "Graceful, then force"), Pair("Script.Stop.Kill", "Force immediately"), Pair("Script.Interpreter.Auto", "Automatic"),
                Pair("Script.Interpreter.VbsConsole", "VBS — cscript.exe"), Pair("Script.Interpreter.VbsWindow", "VBS — wscript.exe"), Pair("Script.Encoding.Auto", "Auto (Windows OEM)"),
                Pair("Script.Encoding.Utf8", "UTF-8"), Pair("Script.Encoding.Oem", "Windows OEM"), Pair("Script.Encoding.Windows1251", "Windows-1251"), Pair("Script.Encoding.Utf16", "UTF-16 LE"),
                Pair("Script.ValidationTitle", "Validate entry"), Pair("Script.FileMissing", "Script file was not found."), Pair("Script.DirectoryMissing", "Working directory was not found."),
                Pair("Script.FileFilter", "Supported scripts|*.cmd;*.bat;*.ps1;*.vbs|All files|*.*"), Pair("Script.SelectFile", "Select a script"), Pair("Script.SelectDirectory", "Select a working directory"),
                Pair("Settings.Title", "CmdsManager settings"), Pair("Settings.Tab.General", "General"), Pair("Settings.Tab.Tools", "Paths and logs"),
                Pair("Settings.StartWithWindows", "Start CmdsManager when signing in to Windows"), Pair("Settings.StartMinimized", "Hide to tray when started manually"),
                Pair("Settings.AutoStartScripts", "Start selected scripts on launch"), Pair("Settings.ConfirmDelete", "Confirm entry deletion"), Pair("Settings.Language", "Language"),
                Pair("Settings.ConsoleFont", "Console font"), Pair("Settings.ChooseFont", "Choose…"), Pair("Settings.Editor", "Editor"), Pair("Settings.EditorArguments", "Editor arguments"),
                Pair("Settings.PowerShell7", "Path to pwsh.exe"), Pair("Settings.Retention", "Keep logs, days"), Pair("Settings.LogOutput", "Write stdout/stderr to log (may contain secrets)"),
                Pair("Settings.Warning", "Auto-start is per-user. Do not move the portable folder after enabling it."), Pair("Settings.ValidationTitle", "Validate settings"),
                Pair("Settings.EditorRequired", "Specify an editor path."), Pair("Settings.EditorMissing", "The editor was not found."), Pair("Settings.PowerShellMissing", "The PowerShell 7 path was not found."),
                Pair("Settings.AppFilter", "Applications|*.exe|All files|*.*"), Pair("Settings.PowerShellFilter", "PowerShell 7|pwsh.exe|Applications|*.exe"),
                Pair("About.Title", "About"), Pair("About.Description", "Manager for CMD, BAT, PowerShell, and VBS scripts"), Pair("About.Version", "Version {0}"),
                Pair("Tray.Toggle", "Open / hide"), Pair("Tray.AutoStartFailed", "Could not auto-start {0} script(s). Open the app for details."),
                Pair("Tray.ExitTitle", "Exit CmdsManager"), Pair("Tray.ExitConfirm", "All scripts started by CmdsManager will be stopped. Exit?"),
                Pair("Tray.Exiting", "CmdsManager — exiting"), Pair("App.UiErrorTitle", "CmdsManager error")
            });
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        private static Dictionary<string, string> Dictionary(IEnumerable<KeyValuePair<string, string>> values)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }
    }
}
