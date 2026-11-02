# Format Wheel · печать (плагин)

Плагин для Format Wheel: добавляет лепесток **«Печать»** в **третье меню** колеса — **Alt + Shift**.
Первое меню (просто Shift) — для конвертации форматов, второе (Ctrl + Shift) — для других действий.

## Как работает

1. Зажмите **Shift**, потяните файл.
2. Держите **Alt** — откроется третье меню, в нём лепесток «Печать».
3. Отпустите **Shift** на лепестке «Печать» — драг Проводника отменится, откроется диалоговое окно печати.
4. В диалоге выберите принтер, нажмите «Свойства» для настройки принтера или «Печать» для начала печати.
5. Прогресс и отмена — в панели у трея.

## Какие файлы печатает

Поддерживаются **фото и документы** (кнопка «Печать» доступна только для них):

| Тип | Расширения |
|-----|------------|
| Фото | `.jpg .jpeg .png .bmp .tif .tiff .gif .webp` |
| Текст | `.txt .log .csv .rtf` |
| Документы | `.pdf .doc .docx .xls .xlsx .ppt .pptx` |

**Не поддерживаются:** видео (`.mp4 .avi .mkv .mov .wmv .flv .webm` и др.) и аудио (`.mp3 .wav .wma .flac .aac .ogg` и др.) — для таких файлов лепесток «Печать» не появляется.

Изображения и текстовые файлы печатаются напрямую через `PrintDocument`. PDF и документы Office печатаются через системную оболочку (команда «print» Windows).

## Диалог печати

При нажатии на «Печать» открывается окно со следующими элементами:

- **Список принтеров** — выбор устройства для вывода
- **«Свойства»** — открывает стандартный диалог свойств выбранного принтера
- **«Печать»** — запуск печати выбранных файлов
- **«Отмена»** — отмена без печати

## Установка

### Через установщик (рекомендуется)

1. Запустите `dist\FormatWheel.Print-Setup-1.0.2.exe`
2. Плагин скопируется в `%LOCALAPPDATA%\Programs\FormatWheel\extensions\FormatWheel.Print\`
3. Нажмите «Готово» и **перезапустите Format Wheel**

### Вручную

1. Соберите: `dotnet build src/FormatWheel.Plugin.Print -c Release`
2. Скопируйте содержимое `bin\Release\net10.0-windows\` (включая `FormatWheel.Sdk.dll`) в:
   - `%LOCALAPPDATA%\Programs\FormatWheel\extensions\FormatWheel.Print\` — доверенные
   - `%APPDATA%\FormatWheel\extensions\FormatWheel.Print\` — пользовательские (требует подтверждения при запуске)
3. Перезапустите Format Wheel.

## Структура плагина

```
src/FormatWheel.Plugin.Print/
├── FormatWheel.Plugin.Print.csproj   — проект плагина (ссылка на FormatWheel.Sdk)
├── PrintActions.cs                   — IWheelActions, Slot = WheelSlot.CtrlAlt, кастомный диалог печати
└── README.md
```

## SDK, используемый плагином

- `FormatWheel.Sdk.IWheelActions` — действие вместо конвертации (ничего не возвращает).
- `FormatWheel.Sdk.WheelAction` — один лепесток-действие; `Slot = WheelSlot.Alt` coloca его в **третье меню** (Alt + Shift).
- `Run` вызывается на отдельном **STA-потоке** — оттуда можно показывать окна (`PrintDialog`, `MessageBox`, своя форма).
- Прогресс и отмена ведутся через `report(0..1)` и `CancellationToken` — панель у трея (`JobsWindow`).

## Сборка установщика

Требуется [Inno Setup 6](https://jrsoftware.org/isdl.php):

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 /DPublishDir="src\FormatWheel.Plugin.Print\bin\Release\net10.0-windows" installer\FormatWheel.Print.iss
```

Результат: `dist\FormatWheel.Print-Setup-1.0.2.exe`
