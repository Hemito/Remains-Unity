using System;
using System.Collections.Generic;
using Remains.Domain;

namespace Remains.Presentation
{
    public enum WorldVisualCategory {Scenery,OrdinaryObject,Body,Npc,Player,Telekinesis,Effect}
    public enum WorldVisualKind {Actor,Prop,Container,Door,Platform,Button,Grenade,Effect,Mine,Hazard}

    // World category is independent of position, ownership, pose and local part order.
    public static class WorldVisualOrderPolicy
    {
        public const int SceneryRoomOrder=0,ActorShadowOrder=1,ActorPartOrder=20,WeaponPartOrder=100,OutlineLocalOrder=short.MaxValue;
        private static readonly Dictionary<string,WorldVisualCategory> actorCategories=new(StringComparer.Ordinal)
        {{"training-dummy",WorldVisualCategory.Scenery},{"training-armored-dummy",WorldVisualCategory.Scenery}};
        public static long Generation {get;private set;}
        public static void RegisterActorCategory(string actorType,WorldVisualCategory category,bool replace=false)
        {
            if(string.IsNullOrWhiteSpace(actorType) || category is not (WorldVisualCategory.Scenery or WorldVisualCategory.Npc))
                throw new ArgumentException("Тип актёра может явно выбирать категорию декорации или живого NPC: "+actorType);
            if(!replace && actorCategories.ContainsKey(actorType)) throw new InvalidOperationException("Повтор категории изображения актёра: "+actorType);
            actorCategories[actorType]=category;Generation++;
        }
        public static WorldVisualCategory Category(ActorState actor)
        {
            if(actor==null) throw new ArgumentNullException(nameof(actor));
            if(ActorHoldRules.Active(actor)) return WorldVisualCategory.Telekinesis;
            if(actor.down || actor.health<=0 || ActorSessionRules.IsDisconnectedBody(actor)) return WorldVisualCategory.Body;
            if(actor.player) return WorldVisualCategory.Player;
            return actorCategories.TryGetValue(actor.actorType,out var category)?category:WorldVisualCategory.Npc;
        }
        public static WorldVisualCategory Category(PropState prop)=>prop.held?WorldVisualCategory.Telekinesis:WorldVisualCategory.OrdinaryObject;
        public static int FirstOrder(WorldVisualCategory category)=>category switch
        {
            WorldVisualCategory.Scenery=>2,WorldVisualCategory.OrdinaryObject=>998,WorldVisualCategory.Body=>5000,
            WorldVisualCategory.Npc=>10000,WorldVisualCategory.Player=>15000,WorldVisualCategory.Telekinesis=>21000,
            WorldVisualCategory.Effect=>30000,_=>throw new ArgumentOutOfRangeException(nameof(category))
        };
        public static int LastOrder(WorldVisualCategory category)=>category switch
        {
            WorldVisualCategory.Scenery=>997,WorldVisualCategory.OrdinaryObject=>4999,WorldVisualCategory.Body=>9999,
            WorldVisualCategory.Npc=>14999,WorldVisualCategory.Player=>20999,WorldVisualCategory.Telekinesis=>29999,
            WorldVisualCategory.Effect=>short.MaxValue-1,_=>throw new ArgumentOutOfRangeException(nameof(category))
        };
        public static bool Contains(WorldVisualCategory category,int order)=>order>=FirstOrder(category) && order<=LastOrder(category);
    }

    // One table per visible world. Reconcile runs on state events; only a changed set
    // or category sorts IDs. Motion/aim/owner packets keep the cached ordering.
    public sealed class WorldVisualOrderTable
    {
        private readonly struct Key : IEquatable<Key>
        {
            public readonly WorldVisualKind kind;public readonly string id;
            public Key(WorldVisualKind kind,string id)
            {if(string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Изображение требует устойчивый ID.");this.kind=kind;this.id=id;}
            public bool Equals(Key other)=>kind==other.kind && string.Equals(id,other.id,StringComparison.Ordinal);
            public override bool Equals(object value)=>value is Key key && Equals(key);
            public override int GetHashCode()=>HashCode.Combine((int)kind,StringComparer.Ordinal.GetHashCode(id));
        }
        private struct Entry {public Key key;public WorldVisualCategory category;public int order;}
        private sealed class Transient {public string level;public WorldVisualCategory category;public int references;}
        private readonly Dictionary<Key,WorldVisualCategory> desired=new(),categories=new();
        private readonly Dictionary<Key,int> orders=new();
        private readonly Dictionary<(Key key,string world),Transient> transients=new();
        private readonly HashSet<Key> worldKeys=new();
        private readonly List<Entry> sorted=new();
        private readonly int[] ranks=new int[7];
        private string level,worldId;
        public string WorldId=>worldId;
        public int Rebuilds {get;private set;}
        public int SparseReconciles {get;private set;}
        private long reconciledPolicy=-1;
        private bool emptySkeleton;
        public event Action Changed;
        // A sparse notification is authoritative for all replaced live parts. Skeleton mechanisms/grenades
        // deliberately remain on the full path until they have an explicit change contract of their own.
        public void Reconcile(WorldState world,string visibleLevel,IReadOnlyCollection<string> changedParts)
        {
            if(world!=null && visibleLevel!=null && worldId==world.worldId && level==visibleLevel && changedParts!=null &&
                reconciledPolicy==WorldVisualOrderPolicy.Generation && emptySkeleton && EmptySkeleton(world))
            {
                bool same=true;
                foreach(string part in changedParts) if(!SamePart(world,part)) {same=false;break;}
                if(same) {SparseReconciles++;return;}
            }
            Reconcile(world,visibleLevel);
        }
        private static bool EmptySkeleton(WorldState world)=>world.movingPlatforms.Count==0 && world.mechanismButtons.Count==0 && world.grenades.Count==0;
        private bool SamePart(WorldState world,string part)
        {
            int colon=part.IndexOf(':');if(colon<0) return true;
            string name=part.Substring(0,colon),id=part.Substring(colon+1);
            WorldVisualKind kind;bool visible=false;WorldVisualCategory category=WorldVisualCategory.OrdinaryObject;
            switch(name)
            {
                case "actors":
                    kind=WorldVisualKind.Actor;var actor=world.Find(id);
                    visible=actor!=null && actor.level==level && ActorPresence.IsPresent(actor);
                    if(visible) category=WorldVisualOrderPolicy.Category(actor);break;
                case "props":
                case "containers":
                    kind=name=="props"?WorldVisualKind.Prop:WorldVisualKind.Container;
                    PropState prop=name=="props"?world.props.Find(value=>value.id==id):world.containers.Find(value=>value.id==id);
                    visible=prop!=null && prop.level==level;if(visible) category=WorldVisualOrderPolicy.Category(prop);break;
                case "doors":
                    kind=WorldVisualKind.Door;var door=world.doors.Find(value=>value.id==id);visible=door!=null && door.level==level;break;
                case "mines":
                    kind=WorldVisualKind.Mine;var mine=world.mines.Find(value=>value.id==id);visible=mine!=null && mine.level==level;break;
                case "hazards":
                    kind=WorldVisualKind.Hazard;var hazard=world.hazards.Find(value=>value.id==id);visible=hazard!=null && hazard.level==level;break;
                default:return true;
            }
            var key=new Key(kind,id);
            if(!visible) return !worldKeys.Contains(key);
            return worldKeys.Contains(key) && categories.TryGetValue(key,out var old) && category==old;
        }
        public void Reconcile(WorldState world,string visibleLevel)
        {
            reconciledPolicy=WorldVisualOrderPolicy.Generation;emptySkeleton=world!=null && EmptySkeleton(world);
            bool identityChanged=worldId!=world?.worldId || level!=visibleLevel;
            worldId=world?.worldId;level=visibleLevel;desired.Clear();worldKeys.Clear();
            if(world!=null && visibleLevel!=null)
            {
                foreach(var actor in world.actors) if(actor.level==visibleLevel && ActorPresence.IsPresent(actor)) Add(WorldVisualKind.Actor,actor.id,WorldVisualOrderPolicy.Category(actor));
                foreach(var prop in world.props) if(prop.level==visibleLevel) Add(WorldVisualKind.Prop,prop.id,WorldVisualOrderPolicy.Category(prop));
                foreach(var container in world.containers) if(container.level==visibleLevel) Add(WorldVisualKind.Container,container.id,WorldVisualOrderPolicy.Category(container));
                foreach(var door in world.doors) if(door.level==visibleLevel) Add(WorldVisualKind.Door,door.id,WorldVisualCategory.OrdinaryObject);
                foreach(var platform in world.movingPlatforms) if(platform.level==visibleLevel && !platform.removed) Add(WorldVisualKind.Platform,platform.id,WorldVisualCategory.OrdinaryObject);
                foreach(var button in world.mechanismButtons) if(button.level==visibleLevel && !button.removed) Add(WorldVisualKind.Button,button.id,WorldVisualCategory.Scenery);
                foreach(var grenade in world.grenades) if(grenade.level==visibleLevel) Add(WorldVisualKind.Grenade,grenade.id,WorldVisualCategory.OrdinaryObject);
                foreach(var mine in world.mines) if(mine.level==visibleLevel) Add(WorldVisualKind.Mine,mine.id,WorldVisualCategory.OrdinaryObject);
                foreach(var hazard in world.hazards) if(hazard.level==visibleLevel) Add(WorldVisualKind.Hazard,hazard.id,WorldVisualCategory.OrdinaryObject);
                foreach(var pair in transients) if(pair.Key.world==worldId && pair.Value.level==visibleLevel)
                {
                    if(desired.TryGetValue(pair.Key.key,out var category) && category!=pair.Value.category)
                        throw new InvalidOperationException("Состояние и изображение расходятся по категории: "+pair.Key.key.id);
                    desired.TryAdd(pair.Key.key,pair.Value.category);
                }
            }
            bool changed=identityChanged || desired.Count!=categories.Count;
            if(!changed) foreach(var pair in desired)
                if(!categories.TryGetValue(pair.Key,out var category) || category!=pair.Value) {changed=true;break;}
            if(changed) Rebuild();
        }
        private void Add(WorldVisualKind kind,string id,WorldVisualCategory category)
        {
            var key=new Key(kind,id);
            if(!desired.TryAdd(key,category)) throw new InvalidOperationException($"Повтор изображения: kind={kind}, id={id}, level={level}.");
            worldKeys.Add(key);
        }
        public bool TryGet(WorldVisualKind kind,string id,out int order,out WorldVisualCategory category)
        {
            var key=new Key(kind,id);category=default;
            return orders.TryGetValue(key,out order) && categories.TryGetValue(key,out category);
        }
        public int GetOrder(WorldVisualKind kind,string id)=>TryGet(kind,id,out int order,out _)?order:
            throw new InvalidOperationException($"Нет порядка изображения: kind={kind}, id={id}, level={level}. Сначала обновите общую таблицу.");
        public WorldVisualCategory GetCategory(WorldVisualKind kind,string id)=>TryGet(kind,id,out _,out var category)?category:
            throw new InvalidOperationException($"Нет категории изображения: kind={kind}, id={id}, level={level}.");
        public string RegisterTransient(WorldVisualKind kind,string id,string effectLevel,WorldVisualCategory category)
        {
            if(worldId==null) throw new InvalidOperationException("Нельзя регистрировать изображение полёта до выбора мира: "+id);
            if(kind!=WorldVisualKind.Effect && kind!=WorldVisualKind.Grenade ||
                category!=(kind==WorldVisualKind.Effect?WorldVisualCategory.Effect:WorldVisualCategory.OrdinaryObject))
                throw new ArgumentException("Только эффекты и реальные полёты гранат имеют временную регистрацию изображения.");
            var key=new Key(kind,id);
            var registration=(key,worldId);
            if(transients.TryGetValue(registration,out var old))
            {if(old.level!=effectLevel) throw new InvalidOperationException("Эффект с тем же ID сменил уровень: "+id);old.references++;return worldId;}
            transients.Add(registration,new Transient {level=effectLevel,category=category,references=1});
            if(effectLevel==level && worldId!=null && desired.TryAdd(key,category)) RebuildCategory(category);
            return worldId;
        }
        public void ReleaseTransient(WorldVisualKind kind,string id,string registeredWorld)
        {
            var key=new Key(kind,id);
            var registration=(key,registeredWorld);
            if(!transients.TryGetValue(registration,out var value)) return;
            if(--value.references!=0) return;
            transients.Remove(registration);
            if(registeredWorld==worldId && !worldKeys.Contains(key) && desired.Remove(key,out var category))
            {categories.Remove(key);orders.Remove(key);RebuildCategory(category);}
        }
        private void Rebuild()
        {
            sorted.Clear();
            foreach(var pair in desired) sorted.Add(new Entry {key=pair.Key,category=pair.Value});
            sorted.Sort(Compare);Array.Clear(ranks,0,ranks.Length);
            // Validate all category capacities before publishing any changed order.
            for(int i=0;i<sorted.Count;i++)
            {
                var entry=sorted[i];entry.order=WorldVisualOrderPolicy.FirstOrder(entry.category)+ranks[(int)entry.category]++;
                if(entry.order>WorldVisualOrderPolicy.LastOrder(entry.category))
                    throw new InvalidOperationException($"Недостаточно порядка изображений: category={entry.category}, count={ranks[(int)entry.category]}, level={level}.");
                sorted[i]=entry;
            }
            categories.Clear();orders.Clear();
            foreach(var entry in sorted) {categories.Add(entry.key,entry.category);orders.Add(entry.key,entry.order);}
            Rebuilds++;Changed?.Invoke();
        }
        // Orders are ranked per category from its own first order, so a transient (a shot's casing or trail) changes
        // only its category: re-rank that one instead of sorting the whole level's table on every effect.
        private void RebuildCategory(WorldVisualCategory category)
        {
            sorted.Clear();
            foreach(var pair in desired) if(pair.Value==category) sorted.Add(new Entry {key=pair.Key,category=category});
            sorted.Sort(Compare);
            int first=WorldVisualOrderPolicy.FirstOrder(category);
            if(sorted.Count>0 && first+sorted.Count-1>WorldVisualOrderPolicy.LastOrder(category))
                throw new InvalidOperationException($"Недостаточно порядка изображений: category={category}, count={sorted.Count}, level={level}.");
            for(int i=0;i<sorted.Count;i++) {categories[sorted[i].key]=category;orders[sorted[i].key]=first+i;}
            ranks[(int)category]=sorted.Count;
            Rebuilds++;Changed?.Invoke();
        }
        private static int Compare(Entry a,Entry b)
        {
            int category=((int)a.category).CompareTo((int)b.category);if(category!=0) return category;
            int id=StringComparer.Ordinal.Compare(a.key.id,b.key.id);return id!=0?id:((int)a.key.kind).CompareTo((int)b.key.kind);
        }
    }
}
