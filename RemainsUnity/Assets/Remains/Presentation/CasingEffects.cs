using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Detached cosmetic particles. They never reconcile restored projectile lists
    // or query the shooter's later transform, weapon selection or ownership.
    public sealed class CasingEffects : MonoBehaviour
    {
        public const int Capacity=64;
        public readonly struct Observation
        {
            public readonly string ShotId,WorldId,Level,Attacker,SourceOwner;
            public readonly Vector2 Socket,Position;
            public readonly float Age,Angle;
            public readonly SpriteRenderer Renderer;
            public Observation(string shot,string world,string level,string attacker,string owner,Vector2 socket,Vector2 position,float age,float angle,SpriteRenderer renderer)
            {ShotId=shot;WorldId=world;Level=level;Attacker=attacker;SourceOwner=owner;Socket=socket;Position=position;Age=age;Angle=angle;Renderer=renderer;}
        }
        private sealed class Slot
        {
            public SpriteRenderer renderer;public Transform transform;
            public string shot,key,world,level,attacker,owner;
            public Vector2 socket,velocity;public float age,angle,lifetime,gravity,rotationSpeed;public bool active;
        }
        private CoopSession session;private WorldVisualOrderTable orders;private string world,level;
        private readonly Slot[] slots=new Slot[Capacity];
        private readonly Dictionary<string,int> active=new(StringComparer.Ordinal);
        private readonly HashSet<string> seen=new(StringComparer.Ordinal),warnings=new(StringComparer.Ordinal);
        private readonly Dictionary<CasingEffectProfile,Material> materials=new();
        public int ActiveCount=>active.Count;
        public int PreparedCount {get;private set;}
        public int CreatedCount {get;private set;}
        public int OverflowCount {get;private set;}
        public void Initialize(CoopSession value,WorldVisualOrderTable orderTable)
        {
            if(session!=null)throw new InvalidOperationException("Эффекты гильз уже инициализированы.");
            session=value??throw new ArgumentNullException(nameof(value));orders=orderTable??throw new ArgumentNullException(nameof(orderTable));
            foreach(var profile in CasingEffectCatalog.Profiles)
            {profile.Validate("подготовка эффекта гильзы");materials.Add(profile,WorldArtworkMaterial.Create(profile.Sprite.texture));}
            for(int index=0;index<Capacity;index++)
            {
                var view=new GameObject("Гильза · "+index);view.transform.SetParent(transform,false);
                var renderer=view.AddComponent<SpriteRenderer>();renderer.enabled=false;renderer.color=Color.white;
                slots[index]=new Slot {renderer=renderer,transform=view.transform};PreparedCount++;
            }
            session.AttackReceived+=Observe;session.Projectiles.Rejected+=Reject;session.Changed+=Reconcile;session.SessionStopped+=Stopped;orders.Changed+=RefreshOrders;Reconcile();
        }
        public void Observe(AttackEvent attack)
        {
            if(attack==null || NpcNaturalAttack.IsNatural(attack) || attack.volley!=null)return;   // one casing per volley
            Reconcile();
            if(session.World==null || attack.level!=level || !CasingEffectCatalog.TryGet(attack.weapon,out var profile))return;
            if(attack.rangedPose==null)
            {Warn("pose:"+attack.weapon,$"Эффект гильзы пропущен: нет сохранённой позы, shot={attack.id}, actor={attack.attacker}, weapon={attack.weapon}, level={attack.level}. Legacy synthetic event не восстанавливает socket из текущего актёра.");return;}
            var pose=attack.rangedPose;pose.Validate("гильза "+attack.id);
            if(pose.worldId!=world)return; // A late event may belong to the previous selected world.
            if(!pose.hasCasing || string.IsNullOrWhiteSpace(attack.id) || string.IsNullOrWhiteSpace(attack.attacker) || string.IsNullOrWhiteSpace(attack.sourceOwner))
                throw new InvalidOperationException($"Нет captured casing context: shot={attack.id}, world={world}, weapon={attack.weapon}, actor={attack.attacker}, owner={attack.sourceOwner}, hasCasing={pose.hasCasing}.");
            if(orders.WorldId!=world)throw new InvalidOperationException("Таблица изображений и captured гильза относятся к разным мирам: shot="+attack.id+", world="+world+", ordersWorld="+orders.WorldId);
            if(!seen.Add(attack.id))return;
            int index=0;while(index<Capacity && slots[index].active)index++;
            if(index==Capacity)
            {OverflowCount++;Warn("capacity","Эффект гильзы пропущен: заняты все "+Capacity+" подготовленных изображений, shot="+attack.id+", world="+world+", level="+level);return;}
            if(!materials.TryGetValue(profile,out var material))throw new InvalidOperationException("Профиль гильзы не подготовлен при Initialize: weapon="+attack.weapon+", profile="+profile.name);
            var slot=slots[index];slot.shot=attack.id;slot.key="casing:"+attack.id;slot.world=world;slot.level=level;slot.attacker=attack.attacker;slot.owner=attack.sourceOwner;
            slot.socket=new Vector2(pose.casingX,pose.casingY);slot.velocity=InitialVelocity(pose.angleDegrees,pose.mirrored,profile.HorizontalSpeed,profile.VerticalSpeed);
            slot.angle=pose.angleDegrees;slot.age=0;slot.lifetime=profile.Lifetime;slot.gravity=profile.Gravity;slot.rotationSpeed=profile.RotationSpeed*(pose.mirrored?-1:1);
            slot.renderer.sprite=profile.Sprite;slot.renderer.sharedMaterial=material;slot.renderer.flipY=pose.mirrored;
            slot.active=true;active.Add(slot.shot,index);
            try {orders.RegisterTransient(WorldVisualKind.Effect,slot.key,slot.level,WorldVisualCategory.Effect);orders.GetOrder(WorldVisualKind.Effect,slot.key);Draw(slot);slot.renderer.enabled=true;RefreshOrders();CreatedCount++;}
            catch {Release(index);throw;}
        }
        // The profile supplies independent Unity decorative constants, not legacy particle steps.
        public static Vector2 InitialVelocity(float angle,bool mirrored,float horizontal,float vertical)
        {
            float radians=angle*Mathf.Deg2Rad,c=Mathf.Cos(radians),s=Mathf.Sin(radians),y=vertical*(mirrored?-1:1);
            return new Vector2(-horizontal*c-y*s,-horizontal*s+y*c);
        }
        public static Vector2 PositionAt(Vector2 socket,Vector2 velocity,float gravity,float age)
            =>socket+velocity*age+Vector2.down*(.5f*gravity*age*age);
        public void Step(float dt)
        {
            if(!float.IsFinite(dt) || dt<0)throw new ArgumentOutOfRangeException(nameof(dt),"Некорректный шаг эффекта гильзы.");
            if(session==null)return;Reconcile();if(level==null || session.Pause.IsPaused(level))return;
            for(int index=0;index<Capacity;index++)
            {
                var slot=slots[index];if(!slot.active)continue;slot.age=Mathf.Min(slot.lifetime,slot.age+dt);
                if(slot.age>=slot.lifetime)Release(index);else Draw(slot);
            }
        }
        private static void Draw(Slot slot)
        {slot.transform.position=PositionAt(slot.socket,slot.velocity,slot.gravity,slot.age);slot.transform.rotation=Quaternion.Euler(0,0,slot.angle+slot.rotationSpeed*slot.age);}
        public bool TryGet(string shotId,out Observation observation)
        {
            observation=default;if(shotId==null || !active.TryGetValue(shotId,out int index))return false;
            var slot=slots[index];observation=new Observation(slot.shot,slot.world,slot.level,slot.attacker,slot.owner,slot.socket,slot.transform.position,slot.age,
                slot.angle+slot.rotationSpeed*slot.age,slot.renderer);return true;
        }
        // Empty disabled pool slots have no artwork by design, not a missing asset.
        public bool IsUnusedRenderer(SpriteRenderer renderer)
        {
            if(renderer==null || renderer.enabled || renderer.sprite!=null)return false;
            foreach(var slot in slots)if(slot?.renderer==renderer)return !slot.active;
            return false;
        }
        private void Reconcile()
        {
            string nextWorld=session.World?.worldId,nextLevel=session.LocalActor?.level;
            if(world==nextWorld && level==nextLevel)return;
            ClearViews();if(world!=nextWorld){seen.Clear();warnings.Clear();}world=nextWorld;level=nextLevel;
        }
        private void Stopped(){ClearViews();seen.Clear();warnings.Clear();world=level=null;}
        private void Reject(string id)
        {if(string.IsNullOrWhiteSpace(id))return;seen.Add(id);if(active.TryGetValue(id,out int index))Release(index);}
        private void ClearViews(){for(int index=0;index<Capacity;index++)if(slots[index]?.active==true)Release(index);}
        private void Release(int index)
        {
            var slot=slots[index];if(!slot.active)return;slot.active=false;slot.renderer.enabled=false;active.Remove(slot.shot);
            orders.ReleaseTransient(WorldVisualKind.Effect,slot.key,slot.world);slot.shot=slot.key=slot.world=slot.level=slot.attacker=slot.owner=null;
        }
        private void RefreshOrders()
        {
            foreach(var slot in slots)
            {
                if(slot?.active!=true)continue;
                if(slot.world!=orders.WorldId || !orders.TryGet(WorldVisualKind.Effect,slot.key,out int order,out _))
                {slot.renderer.forceRenderingOff=true;continue;}
                slot.renderer.sortingLayerID=0;slot.renderer.sortingOrder=order;slot.renderer.forceRenderingOff=false;
            }
        }
        private void Warn(string key,string reason){if(warnings.Add(key))Debug.LogWarning(reason,this);}
        private void OnDestroy()
        {
            if(session!=null){session.AttackReceived-=Observe;session.Projectiles.Rejected-=Reject;session.Changed-=Reconcile;session.SessionStopped-=Stopped;}
            ClearViews();if(orders!=null)orders.Changed-=RefreshOrders;
            foreach(var material in materials.Values){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}materials.Clear();
        }
    }
}
