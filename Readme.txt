CMDS MANAGER 1.1.2
==================

Website: https://github.com/iMiKED/cmds-manager
License: GNU General Public License version 3.0
License text: https://www.gnu.org/licenses/gpl-3.0.html
Author: iMiKED from 4PDA
Author profile: https://4pda.to/forum/index.php?showuser=1017942


ENGLISH
-------

1. DESCRIPTION
--------------

Cmds Manager is a compact portable Windows application for organizing,
starting, monitoring, and stopping CMD, BAT, PowerShell, and VBS scripts.
It stays in the notification area and opens or hides with one left click on
its tray icon.

Supported script types:

- .cmd and .bat through cmd.exe;
- .ps1 through Windows PowerShell 5.1 or PowerShell 7;
- .vbs through cscript.exe or wscript.exe.


2. SYSTEM REQUIREMENTS
----------------------

- Windows 10 or Windows 11 x64;
- .NET Framework 4.8;
- PowerShell 7 is optional and is only needed for entries configured to use it.

The application is portable, does not require installation, and does not
request administrator privileges.


3. INSTALLATION AND FIRST START
-------------------------------

1. Extract the complete ZIP archive to a permanent folder where your user
   account has write access.
2. Run CmdsManager.exe.
3. CmdsManager.ini is created next to the executable on the first start.
4. Add scripts in the main window or edit the INI file and choose Reload INI.

Do not run the application directly from the ZIP archive. Avoid Program Files
unless the selected folder allows the application to update its INI and logs.


4. FEATURES
-----------

- Add, edit, and remove script entries without deleting the script files;
- open script files in a configurable external editor;
- start one script, all enabled scripts, or selected scripts automatically;
- configure application auto-start for the current Windows user;
- configure a global Show App Hotkey that shows and activates the application
  while Cmds Manager is running;
- show a static green execution indicator and detailed runtime state;
- capture stdout and stderr in a fast, bounded console history;
- configure how much console history is kept in memory;
- search the active console with Ctrl+F and move between matches with F3 or
  Shift+F3;
- freeze and resume automatic console scrolling with Scroll Lock;
- record each console to a dedicated UTF-8 log automatically or on demand,
  pause and resume recording, stop it, and enforce a per-file size limit;
- use one neighboring console tab for every managed process instance;
- stop the complete process tree through Windows Job Objects;
- stop all managed scripts when Cmds Manager exits explicitly;
- detach a live console tab into a separate window without restarting the
  process and attach it back by closing the detached window;
- open the active console tab full screen with F11 and leave with Esc or F11;
- resize the console area, maximize it while keeping one scrollable script row,
  and persist the pane height;
- persist the main window position, size, and maximized state;
- copy selected console text;
- save selected text or the complete console contents to a UTF-8 text file;
- select an individual font and output encoding for the active console tab;
- persist Word Wrap per script and inherit it in generated child tabs;
- decode mixed UTF-8, Windows-1251, and Windows OEM output in Auto mode;
- configure console and tab text colors, background colors, and opacity;
- use System, Light, or Dark Fluent Compact application themes;
- switch between English and Russian interface languages stored in the INI;
- show application version, build date and time, author, license, website, and
  donation link in the About window.


5. CONSOLE TABS AND CHILD SCRIPTS
---------------------------------

Every managed launch has a separate console tab. Closing the tab of a running
process requests that exact process to stop.

For captured CMD/BAT scripts, Cmds Manager recognizes the common START form
that launches another .cmd or .bat through cmd /c or cmd /k. The child script
is redirected into a neighboring Cmds Manager tab with its own PID and process
tree. For example:

start "Child" /D "%ROOT_DIR%" cmd /k "%ROOT_DIR%child.cmd"

CALL and ordinary in-process commands continue to use the parent tab because
they share the parent interpreter and its output streams.

Right-click a tab or its console to open commands for search, Scroll Lock,
recording, copying, saving, font, encoding, Word Wrap, detach, full screen,
console-area maximize, clear, and close/stop.


6. OUTPUT ENCODING
------------------

OutputEncoding=Auto validates each captured line as UTF-8 first and then
selects Windows-1251 or the system OEM code page when appropriate. Explicit
choices are Utf8, Oem, Windows1251, and Utf16LittleEndian.

Changing the active tab encoding re-decodes the stored raw output history, so
text already displayed in the tab is corrected as well.


7. LOGS AND PRIVACY
-------------------

Application event logs are stored in the logs folder next to the executable.
When ConsoleAutoRecord=true, every new captured console is written to its own
UTF-8 file under logs\console. Recording can also be started from the console
context menu; a manual start first writes the currently visible buffer and then
continues with new output. Recording can be paused, resumed, or stopped without
stopping the process or its displayed output. ConsoleLogMaxSizeMb is a hard
limit for each file. Files older than LogRetentionDays are removed when console
recording starts.

LogScriptOutput=true additionally writes captured stdout/stderr to the
application event log. This is separate from per-console recording and may
duplicate output. Script output may contain passwords, tokens, personal data,
or other secrets. Enable either form of output logging only when needed.

Cmds Manager does not add script arguments to its event log.


8. SUPPORT THE PROJECT
----------------------

If Cmds Manager saves you time, you can support the author:

- Russia transfers, Ozon Bank / SBP:
  https://finance.ozon.ru/apps/sbp/ozonbankpay/019a0a87-1f4a-7df8-97c7-ef32ebf9a0e3
- VISA: 4400430236422744
- Buy Me a Coffee: https://buymeacoffee.com/danceworldtv
- PayPal: https://paypal.me/imiked
- Boosty: https://boosty.to/danceworldtv/donate
- USDT Ethereum (ERC20): 0xBd0593dDF1DFC7fD95bB6F4e6A5c73Da44048B40
- USDT TON: UQCr2Fp7t34QFuO4IesN3Lo3186a93Z1B7Wu76imr6APIXgk
- USDT Tron (TRC20): TE5A3GT84eJ9iT3mYYLv1KXJnMaiZFxNuA

The Donate button in the About window opens:
https://github.com/iMiKED/cmds-manager?tab=readme-ov-file#support-the-project


9. VERSION HISTORY
------------------

The history below is derived from the Git commits of the application.

1.1.2 - 19.08.2026
- Aligned the Show App Hotkey field with the other settings inputs;
- matched its length to the Console Font field and matched the Clear button to
  the Choose Font button;
- prefilled the disabled-by-default hotkey with Ctrl+Alt+M;
- advanced the INI schema to version 11 and migrated empty legacy hotkeys.

1.1.1 - 19.08.2026
- Added the configurable global Show App Hotkey with a Fluent capture field;
- made the hotkey always show and activate Cmds Manager without hiding an
  already visible window;
- retained the previous registration when a new combination is already used
  by Windows or another application;
- advanced the INI schema to version 10 with automatic migration.

1.1.0 - 18.08.2026
- Added a configurable console history buffer size;
- added separate UTF-8 recording for each console with automatic start,
  pause/resume, stop, retention, and a hard per-file size limit;
- added Ctrl+F search with next/previous navigation and match-case support;
- added Scroll Lock for freezing the console viewport;
- prevented tray restore from leaving the main window at Windows' invisible
  minimized position and stopped invalid off-screen geometry from being saved.

1.0.0 - 17.08.2026
- Added the Built on date and time, evenly aligned information rows, author,
  GPL, website, and Donate information to About;
- added the bilingual user Readme.txt and changed the Portable ZIP contents;
- promoted the application to the first stable release.

0.6.6 - 17.08.2026
- Matched the About Close button to the primary Fluent dialog action style.

0.6.5 - 17.08.2026
- Persisted window layout, console pane height, and per-script Word Wrap.

0.6.4 - 16.08.2026
- Modernized the script editor with Fluent controls.

0.6.3 - 16.08.2026
- Polished Fluent settings input fields.

0.6.2 - 16.08.2026
- Modernized settings controls with Fluent styling.

0.6.1 - 16.08.2026
- Polished the themed toolbar and console tabs.

0.6.0 - 16.08.2026
- Implemented Fluent Compact application themes.

0.5.1 - 16.08.2026
- Polished product branding and console-tab spacing.

0.5.0 - 16.08.2026
- Added detachable and configurable console workspace.

0.4.2 - 16.08.2026
- Replaced the native console TabControl with a custom tab host.

0.4.1 - 16.08.2026
- Redesigned console tabs in a terminal style.

0.4.0 - 16.08.2026
- Enhanced console tabs and the About dialog.

0.3.0 - 16.08.2026
- Improved script consoles and compact dialogs.

0.2.1 - 16.08.2026
- Added running-script indicators.

0.2.0 - 16.08.2026
- Improved console output and localization.

0.1.0-dev - 16.08.2026
- Created the initial Cmds Manager MVP.


10. COMPLETE INI SETTINGS REFERENCE
-----------------------------------

The INI file is UTF-8 and is stored next to CmdsManager.exe. Boolean values are
true or false. Relative paths are resolved from the INI directory. Environment
variables such as %SystemRoot% are expanded.

[Application]

ConfigVersion
  INI schema version maintained by Cmds Manager. Current value: 11. Do not
  lower it manually. Configurations from versions 1 through 10 are migrated to
  11.

Theme
  Application shell theme: System, Light, or Dark. Default: System.

CloseToTray
  true hides the window in the tray when its close button is pressed; false
  requests complete application exit. Default: true.

StartMinimized
  true starts a manually launched application hidden in the tray. Default: false.

StartWithWindows
  true registers Cmds Manager under HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  for the current user. Default: false.

StartHiddenWhenAutoStarted
  true keeps an automatically started instance hidden in the tray. Default: true.

AutoStartScripts
  true starts entries with AutoStartWithApplication=true after application
  startup. Default: true.

ConfirmBeforeDelete
  true asks before an entry is removed. The script file is never deleted.
  Default: true.

ShowAppHotkeyEnabled
  true registers ShowAppHotkey as a Windows global hotkey while Cmds Manager is
  running. The hotkey always shows and activates the application; it never
  hides an already visible window. Default: false.

ShowAppHotkey
  Key combination for Show App Hotkey, for example Ctrl+Alt+M or Shift+Win+F12.
  At least one of Ctrl, Alt, Shift, or Win and one supported non-modifier key
  are required. Configure it in Settings by selecting Show App Hotkey and then
  pressing the combination in the Fluent capture field. If Windows or another
  application already uses it, the new value is not saved and the previous
  registered hotkey remains active. Default: Ctrl+Alt+M. The hotkey itself is
  disabled by default until ShowAppHotkeyEnabled is selected.

MainWindowPlacementSaved
  Internal marker indicating that the window geometry below is valid.

MainWindowX, MainWindowY
  Saved screen coordinates. Allowed range: -100000 through 100000.

MainWindowWidth, MainWindowHeight
  Saved normal window size. Width: 880 through 20000. Height: 520 through 20000.

MainWindowMaximized
  Saved maximized state. The normal restore bounds are preserved separately.

EditorPath
  Executable used to edit scripts. Default: %SystemRoot%\System32\notepad.exe.

EditorArguments
  Editor command-line template. {file} is replaced with the quoted script path.
  If {file} is absent, the path is appended. Default: "{file}".

LogLevel
  Compatibility field for the event-log level. The current release records
  information, warning, and error events. Default: Information.

LogRetentionDays
  Age after which event and console log files are removed. Event logs are
  cleaned at startup; console logs are cleaned when recording starts.
  Allowed range: 1 through 3650 days. Default: 14.

LogScriptOutput
  true writes captured stdout/stderr to the event log. Default: false.

ConsoleFontName
  Default console font family. Default: Consolas.

ConsoleFontSize
  Default console font size in points. Allowed range: 6 through 48. Default: 10.

ConsolePaneHeight
  Saved lower console-area height in pixels. Allowed range: 100 through 4000.
  Default: 235.

ConsoleBufferSizeKb
  Maximum displayed history kept for each console, in KiB. When the limit is
  exceeded, the oldest text is trimmed to leave approximately 75 percent of
  the configured capacity. Allowed range: 64 through 1048576. Default: 256.

ConsoleAutoRecord
  true starts a dedicated UTF-8 log for every new captured console. The files
  are stored under logs\console. Default: false.

ConsoleLogMaxSizeMb
  Hard maximum size of one dedicated console log file, in MiB. Recording stops
  for that console when the limit is reached. Allowed range: 1 through 4096.
  Default: 50.

ConsoleForegroundColor
  Console text color in #RRGGBB format. Default: #DCDCDC.

ConsoleBackgroundColor
  Console background color in #RRGGBB format. Default: #1C1C1C.

ConsoleBackgroundOpacity
  Console background opacity from 0 through 100 percent. Default: 100.

ConsoleTabForegroundColor
  Inactive tab text color in #RRGGBB format. Default: #262B32.

ConsoleActiveTabForegroundColor
  Active tab text color in #RRGGBB format. Default: #F5F7FA.

ConsoleTabBackgroundColor
  Inactive tab background color in #RRGGBB format. Default: #FCFCFD.

ConsoleTabBackgroundOpacity
  Inactive tab background opacity from 0 through 100. Default: 100.

ConsoleActiveTabBackgroundColor
  Active tab background color in #RRGGBB format. Default: #1C1C1C.

ConsoleActiveTabBackgroundOpacity
  Active tab background opacity from 0 through 100. Default: 100.

[Defaults]

This section supplies initial launch values for new entries and fallback values
for script keys that are absent.

Interpreter
  Auto, Cmd, WindowsPowerShell, PowerShell7, CScript, or WScript.
  Default: Auto.

Arguments
  Additional arguments passed after the script path. Default: empty.

WorkingDirectory
  Script working directory. Empty uses the script file directory.

WindowMode
  Hidden, Normal, or Minimized. Default: Hidden.

CaptureOutput
  true redirects stdout/stderr to a Cmds Manager console tab. WScript cannot
  use captured console output. Default: true.

OutputEncoding
  Auto, Utf8, Oem, Windows1251, or Utf16LittleEndian. Default: Auto.

WordWrap
  Initial console Word Wrap state. Default: false.

AllowParallelInstances
  true permits more than one simultaneous launch of an entry. Default: false.

StopPolicy
  GracefulThenKill or Kill. Default: GracefulThenKill.

StopTimeoutSeconds
  Graceful-stop timeout before forced termination. Range: 0 through 3600.
  Default: 5.

[PowerShell]

PowerShell7Path
  Full path to pwsh.exe or its directory. Empty searches PATH and the standard
  Program Files\PowerShell\7 location.

[Localization]

Language
  Selected language section suffix. Built-in values: en and ru. Default: ru.

[Strings.<language>]

These sections contain all visible interface strings. Every key corresponds to
one label, command, status, message, or dialog caption. Copy the complete key set
from Strings.en or Strings.ru when adding another language. Use \n inside an INI
value for a line break. The {0}, {1}, and {2} placeholders must be preserved.
Missing built-in keys are restored automatically without overwriting customized
values. Fixed author, license, website, donation, and payment URLs are not read
from these sections.

[Script:<GUID>]

Each script entry has its own section whose suffix is a unique non-empty GUID.

Name
  Required display name.

Enabled
  true allows the entry to start; false keeps it disabled. Default: true.

Path
  Required .cmd, .bat, .ps1, or .vbs file path.

Interpreter
  Auto, Cmd, WindowsPowerShell, PowerShell7, CScript, or WScript.

Arguments
  Additional script arguments.

WorkingDirectory
  Working directory. Empty uses the script directory.

WindowMode
  Hidden, Normal, or Minimized.

CaptureOutput
  Enables the embedded console tab when supported by the interpreter.

OutputEncoding
  Auto, Utf8, Oem, Windows1251, or Utf16LittleEndian.

WordWrap
  Persistent Word Wrap for this script and its managed child tabs.

AllowParallelInstances
  Allows simultaneous instances of this entry.

AutoStartWithApplication
  Starts this entry when Cmds Manager starts and AutoStartScripts=true.

AutoStartOrder
  Signed 32-bit order value. Lower values start first. Default: 100.

AutoStartDelaySeconds
  Delay before auto-start. Range: 0 through 86400. Default: 0.

StopPolicy
  GracefulThenKill or Kill.

StopTimeoutSeconds
  Graceful-stop timeout in seconds. Range: 0 through 3600.


РУССКИЙ
-------

1. ОПИСАНИЕ
-----------

Cmds Manager — компактное portable-приложение для Windows, предназначенное для
организации, запуска, наблюдения и остановки CMD-, BAT-, PowerShell- и
VBS-скриптов. Приложение живёт в области уведомлений и открывается или скрывается
одиночным щелчком левой кнопки мыши по значку в трее.

Поддерживаемые типы скриптов:

- .cmd и .bat через cmd.exe;
- .ps1 через Windows PowerShell 5.1 или PowerShell 7;
- .vbs через cscript.exe или wscript.exe.


2. СИСТЕМНЫЕ ТРЕБОВАНИЯ
-----------------------

- Windows 10 или Windows 11 x64;
- .NET Framework 4.8;
- PowerShell 7 необязателен и нужен только для записей, где он выбран явно.

Приложение является portable, не требует установки и не запрашивает повышение
прав.


3. УСТАНОВКА И ПЕРВЫЙ ЗАПУСК
----------------------------

1. Полностью распакуйте ZIP-архив в постоянную папку, доступную вашему
   пользователю для записи.
2. Запустите CmdsManager.exe.
3. При первом запуске рядом с EXE будет создан CmdsManager.ini.
4. Добавьте скрипты через главное окно либо измените INI и выберите
   «Перечитать INI».

Не запускайте приложение непосредственно из ZIP. Не размещайте его в Program
Files, если выбранная папка не позволяет обновлять INI и журналы.


4. ВОЗМОЖНОСТИ
--------------

- Добавление, изменение и удаление записей без удаления файлов скриптов;
- открытие скриптов в настраиваемом внешнем редакторе;
- запуск одного скрипта, всех активных скриптов или автоматический запуск
  отмеченных скриптов;
- автозапуск приложения для текущего пользователя Windows;
- глобальный хоткей «Показать приложение», который показывает и активирует
  Cmds Manager, пока приложение запущено;
- статичный зелёный индикатор выполнения и подробное состояние процесса;
- быстрый перехват stdout и stderr с ограниченной историей консоли;
- настройка объёма истории, сохраняемой в каждой консоли;
- поиск в активной консоли по Ctrl+F и переход между совпадениями по F3 либо
  Shift+F3;
- фиксация и продолжение автоматической прокрутки по Scroll Lock;
- автоматическая или ручная запись каждой консоли в отдельный UTF-8-журнал с
  паузой, продолжением, остановкой и ограничением размера файла;
- отдельная соседняя вкладка для каждого управляемого экземпляра процесса;
- остановка всего дерева процессов через Windows Job Objects;
- остановка всех управляемых скриптов при явном выходе из Cmds Manager;
- отделение работающей вкладки в самостоятельное окно без перезапуска процесса
  и возврат вкладки при закрытии отделённого окна;
- полноэкранный режим активной вкладки по F11, выход по Esc или F11;
- изменение размера области консолей, её разворачивание с сохранением одной
  прокручиваемой строки списка и сохранение высоты области;
- сохранение положения, размера и развёрнутого состояния главного окна;
- копирование выделенного текста консоли;
- сохранение выделения или всего содержимого консоли в текстовый файл UTF-8;
- индивидуальный шрифт и кодировка для активной вкладки консоли;
- сохранение Word Wrap для каждого скрипта и наследование в дочерних вкладках;
- декодирование смешанного вывода UTF-8, Windows-1251 и Windows OEM в режиме Auto;
- настройка цветов текста и фона консоли и вкладок, а также непрозрачности;
- системная, светлая и тёмная темы Fluent Compact;
- переключение русского и английского интерфейса со строками в INI;
- отображение версии, даты и времени сборки, автора, лицензии, сайта и ссылки
  для поддержки проекта в окне «О программе».


5. ВКЛАДКИ КОНСОЛИ И ДОЧЕРНИЕ СКРИПТЫ
-------------------------------------

Каждый управляемый запуск получает отдельную вкладку консоли. Закрытие вкладки
работающего процесса запрашивает остановку именно этого процесса.

Для CMD/BAT с перехватом вывода Cmds Manager распознаёт типовую команду START,
запускающую другой .cmd или .bat через cmd /c либо cmd /k. Дочерний скрипт
перенаправляется в соседнюю вкладку Cmds Manager с собственными PID и деревом
процессов. Например:

start "Дочерний" /D "%ROOT_DIR%" cmd /k "%ROOT_DIR%child.cmd"

CALL и обычные внутрипроцессные команды остаются в родительской вкладке,
поскольку используют родительский интерпретатор и его потоки вывода.

Щёлкните вкладку или консоль правой кнопкой мыши, чтобы открыть команды поиска,
Scroll Lock, записи журнала, копирования, сохранения, выбора шрифта, кодировки,
Word Wrap, отделения вкладки, полноэкранного режима, разворачивания области,
очистки и закрытия/остановки.


6. КОДИРОВКА ВЫВОДА
-------------------

OutputEncoding=Auto сначала проверяет каждую перехваченную строку как UTF-8,
затем при необходимости выбирает Windows-1251 или системную OEM-кодировку.
Явные варианты: Utf8, Oem, Windows1251 и Utf16LittleEndian.

При изменении кодировки активной вкладки сохранённая история сырых байтов
декодируется повторно, поэтому исправляется и уже показанный текст.


7. ЖУРНАЛЫ И КОНФИДЕНЦИАЛЬНОСТЬ
-------------------------------

Журналы событий приложения хранятся в папке logs рядом с EXE. Если
ConsoleAutoRecord=true, каждая новая перехватываемая консоль записывается в
отдельный UTF-8-файл в папке logs\console. Запись также можно включить вручную
из контекстного меню: в файл сначала попадёт видимый буфер, затем новый вывод.
Запись можно поставить на паузу, продолжить или остановить, не останавливая
процесс и показ его вывода. ConsoleLogMaxSizeMb задаёт жёсткий предел одного
файла. Файлы старше LogRetentionDays удаляются при начале записи консоли.

LogScriptOutput=true дополнительно записывает перехваченные stdout/stderr в
журнал событий приложения. Эта функция не зависит от отдельной записи консоли
и может дублировать вывод. Вывод может содержать пароли, токены, персональные
данные и другие секреты. Включайте любой вид журналирования только при
необходимости.

Cmds Manager самостоятельно не добавляет аргументы скриптов в журнал событий.


8. ПОДДЕРЖАТЬ ПРОЕКТ
--------------------

Если Cmds Manager экономит ваше время, вы можете поддержать автора:

- Переводы по России, Ozon Банк / СБП:
  https://finance.ozon.ru/apps/sbp/ozonbankpay/019a0a87-1f4a-7df8-97c7-ef32ebf9a0e3
- VISA: 4400430236422744
- Buy Me a Coffee: https://buymeacoffee.com/danceworldtv
- PayPal: https://paypal.me/imiked
- Boosty: https://boosty.to/danceworldtv/donate
- USDT Ethereum (ERC20): 0xBd0593dDF1DFC7fD95bB6F4e6A5c73Da44048B40
- USDT TON: UQCr2Fp7t34QFuO4IesN3Lo3186a93Z1B7Wu76imr6APIXgk
- USDT Tron (TRC20): TE5A3GT84eJ9iT3mYYLv1KXJnMaiZFxNuA

Кнопка «Поддержать» в окне «О программе» открывает:
https://github.com/iMiKED/cmds-manager?tab=readme-ov-file#support-the-project


9. ИСТОРИЯ ВЕРСИЙ
-----------------

История составлена по Git-коммитам приложения.

1.1.2 — 19.08.2026
- Поле хоткея «Показать приложение» выровнено с остальными инпутами настроек;
- его длина приведена к длине поля «Шрифт консоли», а кнопка «Очистить» — к
  длине кнопки «Выбрать…»;
- в выключенный по умолчанию хоткей предварительно подставлено Ctrl+Alt+M;
- схема INI обновлена до версии 11 с миграцией пустых старых значений хоткея.

1.1.1 — 19.08.2026
- Добавлен настраиваемый глобальный хоткей «Показать приложение» с Fluent-полем
  захвата сочетания;
- хоткей всегда показывает и активирует Cmds Manager, не скрывая уже видимое
  окно;
- если новое сочетание занято Windows или другой программой, сохраняется
  предыдущая регистрация;
- схема INI обновлена до версии 10 с автоматической миграцией.

1.1.0 — 18.08.2026
- Добавлена настройка размера отображаемого буфера консоли;
- добавлена отдельная UTF-8-запись каждой консоли с автоматическим запуском,
  паузой/продолжением, остановкой, сроком хранения и жёстким лимитом файла;
- добавлен поиск Ctrl+F с переходом вперёд/назад и учётом регистра;
- добавлен Scroll Lock для фиксации позиции просмотра;
- исправлено восстановление из трея: служебная невидимая позиция свёрнутого
  окна Windows больше не сохраняется, а потерянное окно возвращается на экран.

1.0.0 — 17.08.2026
- В окно «О программе» добавлены строка Built on с датой и временем сборки,
  равномерно выровненные строки, автор, GPL, сайт и Donate;
- добавлен двуязычный пользовательский Readme.txt и изменён состав Portable ZIP;
- приложение выпущено как первая стабильная версия.

0.6.6 — 17.08.2026
- Кнопка Close в About приведена к основному Fluent-стилю диалогов.

0.6.5 — 17.08.2026
- Добавлено сохранение геометрии окна, высоты консольной области и Word Wrap.

0.6.4 — 16.08.2026
- Редактор скрипта переведён на Fluent-контролы.

0.6.3 — 16.08.2026
- Доработаны поля ввода Fluent в настройках.

0.6.2 — 16.08.2026
- Контролы настроек переведены на Fluent-стиль.

0.6.1 — 16.08.2026
- Доработаны тематический toolbar и вкладки консоли.

0.6.0 — 16.08.2026
- Добавлены темы Fluent Compact.

0.5.1 — 16.08.2026
- Доработаны название продукта и отступы вкладок.

0.5.0 — 16.08.2026
- Добавлена отделяемая и настраиваемая область консолей.

0.4.2 — 16.08.2026
- Системный TabControl консоли заменён собственным компонентом вкладок.

0.4.1 — 16.08.2026
- Вкладки консоли переработаны в терминальном стиле.

0.4.0 — 16.08.2026
- Расширены возможности вкладок консоли и окна About.

0.3.0 — 16.08.2026
- Улучшены консоли скриптов и компактные диалоги.

0.2.1 — 16.08.2026
- Добавлена индикация выполняющихся скриптов.

0.2.0 — 16.08.2026
- Улучшены вывод консоли и локализация.

0.1.0-dev — 16.08.2026
- Создан первый MVP Cmds Manager.


10. ПОЛНОЕ ОПИСАНИЕ НАСТРОЕК INI
--------------------------------

INI хранится рядом с CmdsManager.exe в UTF-8. Логические значения записываются
как true или false. Относительные пути отсчитываются от папки INI. Переменные
окружения наподобие %SystemRoot% раскрываются автоматически.

[Application]

ConfigVersion
  Версия схемы INI, которой управляет Cmds Manager. Текущее значение: 11.
  Не уменьшайте её вручную. Конфигурации версий 1–10 мигрируют в версию 11.

Theme
  Тема оболочки: System, Light или Dark. По умолчанию: System.

CloseToTray
  true скрывает окно в трей при нажатии системной кнопки закрытия; false
  запрашивает полный выход. По умолчанию: true.

StartMinimized
  true скрывает в трей приложение, запущенное вручную. По умолчанию: false.

StartWithWindows
  true регистрирует Cmds Manager для текущего пользователя в
  HKCU\Software\Microsoft\Windows\CurrentVersion\Run. По умолчанию: false.

StartHiddenWhenAutoStarted
  true скрывает автоматически запущенное приложение в трее. По умолчанию: true.

AutoStartScripts
  true запускает записи с AutoStartWithApplication=true после старта приложения.
  По умолчанию: true.

ConfirmBeforeDelete
  true запрашивает подтверждение удаления записи. Сам файл не удаляется.
  По умолчанию: true.

ShowAppHotkeyEnabled
  true регистрирует ShowAppHotkey как глобальный хоткей Windows, пока Cmds
  Manager запущен. Хоткей всегда показывает и активирует приложение и никогда
  не скрывает уже видимое окно. По умолчанию: false.

ShowAppHotkey
  Сочетание для хоткея «Показать приложение», например Ctrl+Alt+M или
  Shift+Win+F12. Требуются хотя бы один модификатор Ctrl, Alt, Shift либо Win и
  одна поддерживаемая обычная клавиша. В Настройках установите флажок «Хоткей
  "Показать приложение"», затем нажмите сочетание во Fluent-поле. Если оно уже
  занято Windows или другой программой, новое значение не сохраняется, а
  предыдущий зарегистрированный хоткей продолжает работать. По умолчанию:
  Ctrl+Alt+M. Сам хоткей выключен, пока не установлен ShowAppHotkeyEnabled.

MainWindowPlacementSaved
  Служебный признак того, что сохранённая ниже геометрия окна действительна.

MainWindowX, MainWindowY
  Сохранённые координаты. Допустимый диапазон: от -100000 до 100000.

MainWindowWidth, MainWindowHeight
  Обычный размер окна. Ширина: 880–20000. Высота: 520–20000.

MainWindowMaximized
  Сохранённое развёрнутое состояние. Обычные границы восстановления хранятся
  отдельно в координатах и размерах выше.

EditorPath
  Исполняемый файл редактора. По умолчанию:
  %SystemRoot%\System32\notepad.exe.

EditorArguments
  Шаблон аргументов редактора. {file} заменяется путём скрипта в кавычках.
  Если {file} отсутствует, путь добавляется в конец. По умолчанию: "{file}".

LogLevel
  Поле совместимости для уровня журнала. Текущая версия записывает события
  Information, Warning и Error. По умолчанию: Information.

LogRetentionDays
  Возраст удаления файлов журналов событий и консолей. Журналы событий
  очищаются при запуске приложения, журналы консолей — при начале записи.
  Допустимо: 1–3650 дней. По умолчанию: 14.

LogScriptOutput
  true записывает перехваченные stdout/stderr в журнал. По умолчанию: false.

ConsoleFontName
  Семейство шрифта консоли по умолчанию. По умолчанию: Consolas.

ConsoleFontSize
  Размер шрифта консоли в пунктах: 6–48. По умолчанию: 10.

ConsolePaneHeight
  Сохранённая высота нижней области консолей в пикселях: 100–4000.
  По умолчанию: 235.

ConsoleBufferSizeKb
  Максимальный объём отображаемой истории каждой консоли в КиБ. При превышении
  самая старая часть удаляется так, чтобы осталось около 75 процентов заданного
  объёма. Допустимо: 64–1048576. По умолчанию: 256.

ConsoleAutoRecord
  true автоматически запускает отдельный UTF-8-журнал для каждой новой
  перехватываемой консоли. Файлы хранятся в logs\console. По умолчанию: false.

ConsoleLogMaxSizeMb
  Жёсткий предел размера одного отдельного журнала консоли в МиБ. При достижении
  предела запись этой консоли прекращается. Допустимо: 1–4096.
  По умолчанию: 50.

ConsoleForegroundColor
  Цвет текста консоли в формате #RRGGBB. По умолчанию: #DCDCDC.

ConsoleBackgroundColor
  Цвет фона консоли в формате #RRGGBB. По умолчанию: #1C1C1C.

ConsoleBackgroundOpacity
  Непрозрачность фона консоли: 0–100 процентов. По умолчанию: 100.

ConsoleTabForegroundColor
  Цвет текста неактивной вкладки в формате #RRGGBB. По умолчанию: #262B32.

ConsoleActiveTabForegroundColor
  Цвет текста активной вкладки в формате #RRGGBB. По умолчанию: #F5F7FA.

ConsoleTabBackgroundColor
  Цвет фона неактивной вкладки в формате #RRGGBB. По умолчанию: #FCFCFD.

ConsoleTabBackgroundOpacity
  Непрозрачность фона неактивной вкладки: 0–100. По умолчанию: 100.

ConsoleActiveTabBackgroundColor
  Цвет фона активной вкладки в формате #RRGGBB. По умолчанию: #1C1C1C.

ConsoleActiveTabBackgroundOpacity
  Непрозрачность фона активной вкладки: 0–100. По умолчанию: 100.

[Defaults]

Секция задаёт начальные значения для новых записей и значения отсутствующих
параметров скрипта.

Interpreter
  Auto, Cmd, WindowsPowerShell, PowerShell7, CScript или WScript.
  По умолчанию: Auto.

Arguments
  Дополнительные аргументы после пути скрипта. По умолчанию: пусто.

WorkingDirectory
  Рабочая папка. Пустое значение использует папку файла скрипта.

WindowMode
  Hidden, Normal или Minimized. По умолчанию: Hidden.

CaptureOutput
  true перенаправляет stdout/stderr во вкладку Cmds Manager. WScript не
  поддерживает перехват консольного вывода. По умолчанию: true.

OutputEncoding
  Auto, Utf8, Oem, Windows1251 или Utf16LittleEndian. По умолчанию: Auto.

WordWrap
  Начальное состояние переноса строк. По умолчанию: false.

AllowParallelInstances
  true разрешает несколько одновременных экземпляров. По умолчанию: false.

StopPolicy
  GracefulThenKill или Kill. По умолчанию: GracefulThenKill.

StopTimeoutSeconds
  Ожидание корректной остановки перед принудительным завершением: 0–3600 секунд.
  По умолчанию: 5.

[PowerShell]

PowerShell7Path
  Полный путь к pwsh.exe либо к содержащей его папке. Пустое значение выполняет
  поиск в PATH и в стандартной папке Program Files\PowerShell\7.

[Localization]

Language
  Суффикс выбранной языковой секции. Встроенные значения: en и ru.
  По умолчанию: ru.

[Strings.<язык>]

Эти секции содержат все видимые строки интерфейса. Каждый ключ соответствует
подписи, команде, состоянию, сообщению или заголовку. Для добавления языка
скопируйте полный набор ключей из Strings.en либо Strings.ru. Для перевода строки
используйте \n. Заполнители {0}, {1} и {2} необходимо сохранять. Отсутствующие
встроенные ключи восстанавливаются автоматически без перезаписи изменённых
значений. Фиксированные URL автора, лицензии, сайта, донатов и платёжных способов
из этих секций не читаются.

[Script:<GUID>]

Каждая запись находится в собственной секции, суффикс которой является
уникальным непустым GUID.

Name
  Обязательное отображаемое название.

Enabled
  true разрешает запуск записи; false отключает её. По умолчанию: true.

Path
  Обязательный путь к файлу .cmd, .bat, .ps1 или .vbs.

Interpreter
  Auto, Cmd, WindowsPowerShell, PowerShell7, CScript или WScript.

Arguments
  Дополнительные аргументы скрипта.

WorkingDirectory
  Рабочая папка. Пустое значение использует папку скрипта.

WindowMode
  Hidden, Normal или Minimized.

CaptureOutput
  Включает встроенную консоль, если интерпретатор поддерживает перехват.

OutputEncoding
  Auto, Utf8, Oem, Windows1251 или Utf16LittleEndian.

WordWrap
  Сохраняемый перенос строк для скрипта и его управляемых дочерних вкладок.

AllowParallelInstances
  Разрешает одновременные экземпляры этой записи.

AutoStartWithApplication
  Запускает запись при старте Cmds Manager, если AutoStartScripts=true.

AutoStartOrder
  Знаковое 32-битное значение порядка. Меньшие значения запускаются раньше.
  По умолчанию: 100.

AutoStartDelaySeconds
  Задержка автозапуска: 0–86400 секунд. По умолчанию: 0.

StopPolicy
  GracefulThenKill или Kill.

StopTimeoutSeconds
  Ожидание корректной остановки: 0–3600 секунд.


Copyright (C) 2026 iMiKED.
Cmds Manager is free software distributed under GNU GPL v3.0.
