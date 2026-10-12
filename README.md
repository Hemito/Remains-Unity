# Remains: Unity

Экспериментальный независимый фанатский remake игры **Remains** автора **Empalu**
на Unity с сетевым кооперативом. Это не официальный продукт оригинальной команды.
Авторство оригинальных графики, звуков и контента не приписывается Hemito.

Проект незавершён: это рабочий эксперимент, а не полный перенос оригинала или
стабильный релиз. Multiplayer работает на Mirror/KCP; отдельные кооперативные
сценарии проверены двумя процессами на одном ПК. LAN на двух физических ПК,
четыре игрока и большие сеансы пока не объявляются проверенными.

## Исходники и сборка

Нужны Windows и **Unity 6000.0.39f1** с поддержкой Windows Build.
Открывайте каталог **RemainsUnity**. Mirror **96.11.3** включён в Assets с
лицензиями; Unity Packages закреплены в manifest.json и packages-lock.json.
Сохраняйте файлы `.meta` и версии зависимостей.

Для скачивания исходников нужен Git LFS:

```powershell
git lfs install
git clone https://github.com/Hemito/Remains-Unity.git
cd Remains-Unity
git lfs pull
```

Вместо текстового LFS-указателя Unity должен получить полный игровой ресурс.
После импорта проекта собирайте платформу Windows x86-64 с единственной
стартовой сценой `Assets/Remains/Scenes/Remains.unity`, включённой в Build Settings.

Для фоновой сборки уже подготовленных ресурсов можно вызвать существующий метод:

```powershell
& '<путь к Unity.exe>' -batchmode -quit -projectPath './RemainsUnity' -executeMethod Remains.Editor.ActorProjectedShadowChecks.BuildVerifiedPlayer -logFile './build.log'
```

Результат — `RemainsUnity/Builds/Windows/Remains.exe` с полным каталогом сборки.
Готовому игроку Git LFS не нужен. Эта поставка позволяет собрать существующую игру;
часть Editor-команд переэкспорта и расширенных проверок требует исходных материалов,
которые не распространяются вместе с игровым проектом. Для обычной сборки их не запускают.

Для локального подключения клиент использует `127.0.0.1`; для LAN — IP компьютера
хоста. Транспорт KCP, обычный порт UDP 7777. Участникам нужны совместимые сборки.

## Участие

Сообщество может предлагать Issues и Pull Requests:
[CONTRIBUTING.md](CONTRIBUTING.md). Состояние игры: [PROJECT_STATUS.md](PROJECT_STATUS.md).

## Лицензирование

Собственный код — **PolyForm Noncommercial 1.0.0** с отдельными разрешениями на
добровольные пожертвования, монетизированные видео/стримы и бесплатные зеркала.
Область и стандартные исключения: [LICENSING.md](LICENSING.md).
Дополнительные права: [ADDITIONAL_PERMISSIONS.md](ADDITIONAL_PERMISSIONS.md).
Это модель доступных исходников с ограничениями, не лицензия OSI open source.

Лицензия кода не покрывает оригинальные и другие сторонние ресурсы. Их собственные
условия и права сохраняются: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
