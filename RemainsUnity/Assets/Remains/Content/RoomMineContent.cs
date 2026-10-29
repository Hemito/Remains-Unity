using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class MineLayout
    {
        public string patch,level;
        public ContentExpansion expansion;
        public MineState[] mines=Array.Empty<MineState>();
    }

    // A level's mines as content placements (installed once per patch, then saved world state).
    public sealed class RoomMineContent : MonoBehaviour
    {
        [SerializeField,InspectorName("Версия расстановки мин")] private string patch;
        [SerializeField,InspectorName("Добавочное обновление мин")] private ContentExpansion expansion=new();
        public void Configure(string value)=>patch=value;
        public void ConfigureExpansion(ContentExpansion value)=>expansion=StateJson.Copy(value);
        public static MineLayout Read(RoomAuthoring room,string level)
        {
            var content=room.GetComponent<RoomMineContent>();
            if(content==null || !content.enabled) return null;
            if(string.IsNullOrWhiteSpace(content.patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException($"Комната {room.name}: не задан уровень или версия расстановки мин.");
            return new MineLayout {patch=level+":"+content.patch,level=level,expansion=ContentExpansionRules.InLevel(content.expansion,level),
                mines=room.GetComponentsInChildren<RoomMinePlacement>(true).Where(room.Included).Select(p=>p.Read(room,level)).ToArray()};
        }
        private void OnValidate() {if(string.IsNullOrWhiteSpace(patch)) Debug.LogWarning($"Комната {name}: не задана версия расстановки мин.",this);}
    }

    public sealed class RoomMinePlacement : MonoBehaviour
    {
        [SerializeField,InspectorName("Устойчивый ID мины")] private string stableId;
        [SerializeField,InspectorName("ID оружия-мины")] private string definition;
        public void Configure(string id,string weapon) {stableId=id;definition=weapon;}
        public MineState Read(RoomAuthoring room,string level)
        {
            var rect=RoomAuthoring.ReadRect(transform,room.transform);
            if(string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(definition))
                throw new InvalidOperationException($"Мина {name}: нужны устойчивый ID и оружие.");
            var mine=new MineState {id=level+":"+stableId,level=level,definition=definition,side=MineRules.LevelSide,x=rect.x,y=rect.y};
            MineRules.Validate(mine);MineRules.Profile(mine);
            return mine;
        }
    }

    public static class MineContentInstaller
    {
        public static bool NeedsInstall(WorldState world,MineLayout layout)=>layout!=null && !world.contentPatches.Contains(layout.patch);
        public static bool Apply(WorldState world,MineLayout layout)
        {
            if(world==null || layout==null || string.IsNullOrWhiteSpace(layout.patch) || string.IsNullOrWhiteSpace(layout.level) || layout.mines==null)
                throw new InvalidOperationException("Неполная расстановка мин.");
            if(!NeedsInstall(world,layout)) return false;
            // An older world gets only the additions of its version (ContentExpansionRules): a mine it lost stays lost.
            var selected=ContentExpansionRules.SelectedIds(world,layout.patch,layout.level,layout.expansion,layout.mines.Select(m=>m?.id));
            if(selected!=null) layout=new MineLayout {patch=layout.patch,level=layout.level,mines=layout.mines.Where(m=>selected.Contains(m.id)).ToArray()};
            ContentEntityIds.Preflight(world,layout.mines.Select(m=>new ContentEntityIdentity(ContentEntityKind.Mine,m?.id)),layout.patch);
            foreach(var mine in layout.mines)
            {
                if(mine==null || mine.level!=layout.level) throw new InvalidOperationException("Мина другой локации в расстановке: "+mine?.id);
                MineRules.Validate(mine);
                // A mine already in the world (saved state: triggered, or removed by its explosion) is not placed again.
                if(world.mines.Exists(saved=>saved.id==mine.id)) continue;
                world.mines.Add(StateJson.Copy(mine));
            }
            world.contentPatches.Add(layout.patch);world.revision++;
            return true;
        }
    }

    // The generated factory's mines from its room templates (original Location.createUnit): a "mine" object is one
    // level mine (Mine.as without tr: hmine), a "mines" minefield three of them two cells apart along the source row.
    // v2 adds the random mines of the trap points (FactoryRandomEncounters: "mine" units, mine or hmine by randomCid).
    public static class FactoryMineAdapter
    {
        public const string PatchV1="base:factory-generated:mines-v1",Patch="base:factory-generated:mines-v2";
        public const string LevelMine="hmine";
        public static int Install(FactoryLevelRoot root,IReadOnlyList<FactoryRandomEncounters.Encounter> plan)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Мины завода: нужен подготовленный корень.");
            if(root.GetComponent<RoomMineContent>()!=null) throw new InvalidOperationException("Мины завода: повторная установка запрещена.");
            WeaponCatalog.Get(LevelMine);
            int count=0;
            foreach(var placement in root.Objects.OrderBy(value=>value.Id,StringComparer.Ordinal))
            {
                string type=placement.Source.Type;
                int mines=type=="remains:obj:mine"?1:type=="remains:obj:mines"?3:0;
                if(mines==0) continue;
                if(placement.Source.Params.Count>0 || placement.Source.Items.Count>0 || placement.Source.Links.Count>0 || placement.Source.Extra!=null)
                {Debug.LogWarning($"Мина завода с параметрами или связями пока не поддержана и пропущена: {placement.Id} ({type}).");continue;}
                float cell=(float)placement.Room.Template.Cell;
                float step=(placement.Room.Cell.Mirror?-2:2)*cell;
                string localId=$"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}";
                for(int i=0;i<mines;i++)
                {
                    var node=new GameObject("Мина: "+placement.Id+(mines>1?"/"+i:""));
                    node.transform.SetParent(root.transform,false);node.transform.localPosition=placement.Position+Vector2.right*(step*i);
                    node.AddComponent<RoomMinePlacement>().Configure(mines>1?localId+"/"+i:localId,LevelMine);
                    count++;
                }
                root.HideObjectArtwork(placement);
            }
            var random=new List<string>();
            foreach(var encounter in plan.Where(e=>e.Kind==RandomEncounterRules.Trap && e.Unit=="mine"))
            {
                string definition=encounter.Variant??throw new InvalidOperationException("Мины завода: у случайной мины не выбран вариант: "+encounter.LocalId);
                WeaponCatalog.Get(definition);
                var node=new GameObject("Случайная мина: "+encounter.Point.Id);
                node.transform.SetParent(root.transform,false);node.transform.localPosition=encounter.Point.Position;
                node.AddComponent<RoomMinePlacement>().Configure(encounter.LocalId,definition);
                random.Add(encounter.LocalId);count++;
            }
            // A world of v1 gets only the random mines; without any, the layout is v1 as before.
            var content=root.gameObject.AddComponent<RoomMineContent>();content.Configure(random.Count>0?Patch:PatchV1);
            if(random.Count>0) content.ConfigureExpansion(new ContentExpansion {previousPatch=PatchV1,additions=random.ToArray()});
            Debug.Log($"Завод {root.Prepared.Physics.id}: случайных мин поставлено {random.Count}.");
            return count;
        }
    }
}
