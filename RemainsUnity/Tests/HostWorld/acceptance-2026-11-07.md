# TD-33: итог проверки 2026-11-07

Реализован один авторитетный граф хоста; `World` и `ServerWorld` совпадают, собственные WorldPart/WorldTick не отправляются и не применяются. Development/Editor сохраняют самоотправку, явные `--host-self-ticks on|off` работают. Серверные проверки удалённых сообщений, протокол 98 и формат сохранений сохранены. Указанный в исходном handoff протокол99 был неточным: фактический `SessionProtocol.Version` равен98.

Основная ветка: `feat/td33-host-single-world`, код `fb01263e8`, затем README-only `04b503304`. Отдельные необходимые исправления: `fix/held-prop-landing-observation` (`d0a2db772`), `test/weapon-reload-capture-pause` (`1cda89e4c`). Они не включены в основную TD33-ветку; объединены только для проверки в `test/td33-final-acceptance` (`e94af0f6b`). Перенос Native fixture tool: отдельная `test/linux-seed104-player-fixture` (`823d6ea3ddcc495f3ab553f16ad08e9ae01bf106`). Все ветки отправлены в KalmarCoDna fork, main/upstream и публикация среды не изменялись.

## Native матрица

Все перечисленные прогоны — настоящие графические Linux Players, отдельные сохранения/порты; без физического второго ПК и аппаратного D3D.

| Проверка | Проверенные режимы и результат | Точная квитанция |
|---|---|---|
| appearance, grenade, обычный telekinesis | OFF/ON PASS; код15ca6ea9a | `/workspace/scratch/td33-native-final/summary.json` — учитывать только успешные строки |
| projectile-handoff | OFF/ON PASS наcd2a213d6, OFF повторён15ca | `/workspace/scratch/td33-native-cd2a213d6/summary.json` |
| npc, melee, revival | OFF/ON 6/6 PASS; код37f299c91 | `/workspace/scratch/td33-acceptance-final/summary.json` |
| door, container | OFF/ON 4/4 PASS; тот же37f Player | `/workspace/scratch/td33-acceptance-ui-batch/summary.json` |
| shortcuts | OFF/ON PASS; тот же37f Player | `/workspace/scratch/td33-acceptance-ui-frames/summary.json` — только строкиshortcuts |
| contact-audio-targets | OFF/ON PASS, реальные1920 кадры/голоса; кодfb01263 | `/workspace/scratch/td33-strict-final/summary.json` — только строкиtargets |
| prop-body, contact-audio-props, weapon-reload-frames | OFF/ON 6/6 PASS; кодe94af0f6b, DLL9b329b2905ac180916227b1d8446371e1aab69f68c353a9ac4029001432be84c | `/workspace/scratch/td33-cosmetic-reload-final/summary.json` |
| movable-container TK, pause TK, pause | Development БЕЗ mode-аргумента: observedON, 3/3 PASS; e94af | `/workspace/scratch/td33-development-default/summary.json` |
| Эти же TK/pause в OFF | TK-варианты15ca и pause34af PASS | `/workspace/scratch/td33-native-final/summary.json`, `/workspace/scratch/td33-final-bounded/summary.json` |
| factory-number и полный перезапуск | ON/OFF 4/4 PASS; логи режима совпадают с аргументами, worldID сохраняется после перезапуска | `/workspace/scratch/td33-factory-number/summary.json` |
| Релизное умолчание без аргумента | Старый nondev15ca Player: appearance/door PASS, observedOFF | `/workspace/scratch/td33-release-default/summary.json`; финальный общий Release отдельно проверяет root |

Native Editor проверки прошли: launch options; отсутствие изменения канонического JSON при отрисовке/интерполяции30/60/144Гц; совпадение оружия/предмета с проекцией тела; shared death-loot ровно один раз без изменения списка актёров; существующие CreatureDeathLoot/RemoteActorMotion/ActorMotionReplication/WorldReplication/OwnershipStability; реальный MovementController на3/5/7/10/15/30/60/144Гц; падение .75м за один шаг3Гц и защитные проверки аудионаблюдения. Логи: `td33-final-validation-build.log`, `td33-probe-route-kernel3.log`, `td33-core-reentrancy-checks2.log` в `/workspace/scratch`.

## Отдельные исправления и доказательства

Старый MotionContacts терял реальное приземление, если первое наблюдение поколения после отпускания уже стояло на полу. Это воспроизведено старой логикой и реальной физикой3Гц. Отдельное косметическое исправление переносит только наблюдённую высоту удержания при том же мире/владельце/уровне и generation+1, внутри teleport distance; сохранены отказы при смене мира/владельца/уровня/эпохи, reconnect, паузе, телепорте и повторном grounded sample. Исходные короткие подъёмы и требование одного реального голоса не изменены. OFF и ON propbody/contactprops прошли после исправления.

Stock reload probe иногда терял кадр у источника, ожидая35% прогресса до сетевого запроса паузы. Отдельный test-only фикс запрашивает настоящую паузу сразу после настоящего start token. Сохранены4выстрела/4start/stop,2complete/2cancel,8реальных PNG1920, действительный muzzle/renderer и точный расход/сохранение боезапаса. Оба режима прошли.

Ранее ошибочные NPC отчёты были следствием неканонических имён отчётов runner: исходная проба читает `session-client-1-report.json.interrupted-attack.txt`. Имена исправлены; исходные NPC assertions сохранены. UI synthetic pointer требует IgnoreFocus только в opt-in пробе. Fixed-axis маршруты зацикливались при низком FPS; заменены только probe-команды на дробные с реальным dt, без изменения MovementController и допусков. Коротким45/60сек пробам оставлена параллельная начальная загрузка; admission wait — опция только для длинных проб.

## Финальный Native seed104

Чистый Editor тоже выдал content fingerprint473326B6..., не совпадающий с Native5329FA4F.... Первоначальная гипотеза о загрязнении unit registry НЕ подтвердилась. Обе Editor подготовки отвергнуты; ни ee1cfec9..., ни55827c0b... не являются финальным проверенным входом. Они и первоначальные логи сохранены. Причина различия Editor/Native actor/room-object fingerprints не исправлялась и не маскировалась.

Реальное решение — уже существующий Native generator, разрешённый отдельным test-only коммитом823d для WindowsPlayer/LinuxPlayer. Все batch/host/empty/non-nested/report guards сохранены; реальная Editor rejection не создала файлов. Native buildexit0, DLL `fb3e94774b5c12cfae34853470e6bb3fa96f0a3e19b53d55ee07a171f07c75a1`. Новый immutable input:

- `/workspace/scratch/td33-final-native-fixture-input/world.json`
- `/workspace/scratch/td33-final-native-fixture-input/character.json`
- combined SHA256 `ea8b6264ba50a6ea98aec1795ad8608f80bc3e110e13b045375da867e3e310fc`
- настоящий runtime content fingerprint `5329FA4FC1D8B4653B58AD8F5A5DFBB504ECE64D0B3AE02D3661CB65CEBE7DD6`
- factory0:568NPC,1106containers,74mines,53hazards.

Оба измерения стартовали с побайтных копий этих JSON и прошли строгий manifest guard. Initial overlapping-start pair дала hostexit0, но guest KCPcold-timeout; она правильно помечена НЕпарной в `/workspace/scratch/td33-final-native-paired`. Единственный повтор запускал гостя после self-admission хоста, одинаковые warm300/sample180. Финал2/2 paired PASS, оба гостя вошли за35–43сек ДО захвата, все процессы завершены.

Квитанции: `/workspace/scratch/td33-final-native-paired-ready-start/summary.json`, `on/host-report.json`, `off/host-report.json`, `post-eval.json`, скопированные `on/host-frames.txt` и `off/host-frames.txt`.

| Измерение | ON | OFF |
|---|---:|---:|
| sample frames |180|180|
| captured seconds / actual shots |26.108 /35|23.511 /36|
| host-local WorldTickMessage calls |69|0|
| apply copy: mean ms/call |4.332|0 calls|
| working copy: mean ms/call |1.953|0 calls|
| client Changed publication: mean ms/call |11.095|0 calls с этим client label|
| softwareGL wall-frame mean /p95 ms |143.55 /486.66|130.40 /531.31|
| unforced managed memory в capture, MiB |839.3;895.8|908.5;911.9;986.1|

Исчезновение host-local tick/copy секций подтверждено. Publication включает подписчиков, которые продолжают работать по shared пути; его время нельзя целиком объявлять сэкономленным. WorldTickDiagnostics не имеет отдельного timer scope: его время включено в родительский обработчик; за весь запуск записано36slow-tick notes вON и0вOFF. Их нельзя выдавать за точный CPU замер диагностики только внутри capture.

Это llvmpipe/OpenGLCore, actual batch screen640×480 несмотря на CLI1280×720. Wall frame и `work=frame−TimeUpdate` не являются чистым CPU. Allocation metric недоступен: нулевые allocatedBytes недостоверны. GC.GetTotalMemory(false) без принудительного GC и разные естественные фазы сборки не доказывают уменьшение всей памяти. Один paired sample с разным elapsed/shot count не даёт обещания прироста FPS, улучшенияp95 или аппаратного D3D. Сценарий — пистолет у входа и idle peer в хабе, не бой4игроков.

Воспроизводимость: `/workspace/scratch/run-td33-native-baseline.py` (generator +initial pair), `/workspace/scratch/run-td33-native-baseline-ready.py` (финальный повтор), `/workspace/scratch/HostNativeFixturePortBuild.cs`, команды/логи в `td33-final-native-generator`, SHA256SUMS/README в immutable input. Native fixture новый; совпадение со старым Editor baseline не заявляется.

## Пределы и сохранность

Протокол100 grenade-telekinesis — отдельная опубликованная feature dependency, не отсутствующая private probe. Root/factory владеют её совместимой веткой и genuine проверкой; обычный grenade не посчитан вместо неё. Root также владеет frozen общим Release df01532a0/C#dcc91aa16, Windows/Linux сборками и точной финальной Release приёмкой.

Все meaningful saves/logs/reports сохранены. Только5моих неактивных устаревших generated Players удалены после сохранения DLL/PDB и SHA/source receipts в `/workspace/scratch/td33-obsolete-build-receipts`. Сохранены текущие FinalValidated, NativeBaseline и проверенный nondev Release. Полный журнал фактов/решений: `/workspace/scratch/host-single-world-journal.md`.
