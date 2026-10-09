using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Stage A check (docs/pipelines/room-format.md §8): physics generated from the converted
    // grid must cover exactly the cells of the hand-authored room prefabs that ship today.
    // Explicit ladder landings are reviewed data outside the grid and are excluded.
    public static class RoomTemplateChecks
    {
        private const string Definitions="Assets/Remains/Rooms/Original/definitions.json";
        private static readonly (string template,string prefab)[] Pairs=
        {
            ("Assets/Remains/Rooms/Original/plant/tsekh1.room.json","Assets/Remains/RoomDrafts/FactoryWorkshop18.prefab"),
            ("Assets/Remains/Rooms/Original/plant/vkhod2.room.json","Assets/Remains/RoomDrafts/FactoryEntry46.prefab"),
        };

        [MenuItem("Remains/Комнаты/Сверить шаблоны с префабами завода")]
        public static void RunFromMenu()=>Run(false);

        // Batchmode: -executeMethod Remains.Editor.RoomTemplateChecks.RunBatch
        public static void RunBatch()=>Run(true);

        private static void Run(bool exit)
        {
            bool ok=true;
            try
            {
                var definitionsAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Definitions);
                if(definitionsAsset==null) throw new InvalidOperationException("Нет каталога определений комнат: "+Definitions);
                var definitions=RoomDefinitionCatalog.Read(definitionsAsset.text,Definitions);
                foreach(var (templatePath,prefabPath) in Pairs) ok&=Compare(templatePath,prefabPath,definitions);
            }
            catch(Exception error) {Debug.LogError("Сверка шаблонов комнат прервана: "+error);ok=false;}
            Debug.Log(ok?"Сверка шаблонов комнат: PASS":"Сверка шаблонов комнат: FAIL");
            if(exit) EditorApplication.Exit(ok?0:1);
        }

        private static bool Compare(string templatePath,string prefabPath,RoomDefinitionCatalog definitions)
        {
            var template=AssetDatabase.LoadAssetAtPath<RoomTemplateAsset>(templatePath);
            if(template==null) throw new InvalidOperationException("Шаблон не импортирован как RoomTemplateAsset: "+templatePath);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var authoring=prefab==null?null:prefab.GetComponentInChildren<RoomAuthoring>(true);
            if(authoring==null) throw new InvalidOperationException("Нет RoomAuthoring в префабе: "+prefabPath);

            var generated=RoomTemplateLayout.Create(template,definitions);
            var shipped=authoring.CreateLayout(false);
            int landings=shipped.platforms.Count(IsLanding);
            var result=RoomCoverage.Compare(
                shipped.platforms.Where(p=>!IsLanding(p)).Select(Rect),generated.platforms.Select(Rect),
                shipped.ladders.Select(Ladder),generated.ladders.Select(Ladder),template.Room.Cell);
            Debug.Log($"{template.Template} ↔ {prefabPath}: клетки {result.MatchedCells}/{result.ExpectedCells} (получено {result.ActualCells}), лестницы {result.ActualLadders}/{result.ExpectedLadders}, исключено явных площадок {landings}, различий {result.Differences.Count}.");
            foreach(string difference in result.Differences.Take(40)) Debug.LogWarning($"{template.Template}: {difference}");
            return result.Equal;
        }

        // Reviewed explicit landings (Resources/Content/factory-ladder-landings.json) carry their own art key.
        private static bool IsLanding(RoomSurface surface)=>surface.texture!=null && surface.texture.StartsWith("LadderLandings/",StringComparison.Ordinal);

        private static CoverageRect Rect(RoomSurface s)=>new CoverageRect
        {X=s.x,Y=s.y,Width=s.width,Height=s.height,OneWay=s.oneWay,Slope=s.slopeDirection,Sound=s.movementSound??""};

        private static CoverageLadder Ladder(RoomLadder l)=>new CoverageLadder
        {X=l.x,Bottom=l.bottom,Top=l.top,Facing=(int)l.facing,MovementOffset=l.movementOffset};
    }
}
