МОДЫ ADDRESS
Каждая папка здесь - мод. Внутри:
  mod.json            {"name": "...", "author": "...", "version": "1.0", "description": "..."}
  worlds/*.json       новые уровни (id - 2-3 заглавные латинские буквы)
  entities/*.json     новые существа: model (glb/gltf с анимациями), height, walkSpeed, chaseSpeed, noticeRange,
                      freezeWhenSeen, idle/walk/run (части имён анимаций), sounds (wav/ogg), worlds (где живёт), weight
  models/, textures/, sounds/  ваши файлы
Уровень: layout = rooms | corridor | hotel | halls | vast; kinds = office, storage, lounge, breakroom, meeting, server,
dump, pillars, wrong, showroom, library, empty, corridor; wall/floor/ceiling = имя встроенной поверхности
(Wallpaper001A, Carpet006, Concrete034, Tiles074, Rock030, WoodFloor041, ...) или "file:textures/моя.png";
lightColor [r,g,b], lightIntensity, deadShare, flickerShare, noLamps, pipes, water, doors, lockers, ambient, fogColor,
fogDensity, gradeFilter, gradeSaturation, gradeContrast, gradeTemperature, gradeExposure, entities, music, weight.
Пример - папка Extra_Monsters. Меню -> Модификации: включить/выключить.
