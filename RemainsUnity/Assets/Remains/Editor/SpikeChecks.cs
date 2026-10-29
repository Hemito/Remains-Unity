using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // Spikes: the catalog is the original's (AllData spikes/fspikes), the seed104 factory carries every template spike as
    // level geometry with unique IDs and the box of its definition, and the rules are Trap.as — the fall threshold and
    // damage by speed, the head-first rise, who is never hurt, typed armor, the contact cooldown.
    public static class SpikeChecks
    {
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var spikes=SpikeCatalog.Get("spikes");var ceiling=SpikeCatalog.Get("fspikes");
            Require(spikes.attack==SpikeDefinition.Fall && Mathf.Approximately(spikes.damage,50) && spikes.damageType==DamageTypes.Blade &&
                Mathf.Approximately(spikes.width,.8f) && Mathf.Approximately(spikes.height,.8f) && !spikes.bottomAnchored,"шипы из AllData (att 1, урон 50, лезвие, 40×40 px)");
            Require(ceiling.attack==SpikeDefinition.Rise && Mathf.Approximately(ceiling.damage,15) && ceiling.damageType==DamageTypes.Blade &&
                Mathf.Approximately(ceiling.height,.4f) && ceiling.bottomAnchored,"потолочные шипы из AllData (att 2, урон 15, sY 20, floor)");
            CheckRules(spikes,ceiling);
            CheckFactory();
            Debug.Log("Шипы: PASS — каталог оригинала, расстановка seed104, падение/подъём, исключения, броня, длительность неуязвимости.");
        }

        private static void CheckRules(SpikeDefinition spikes,SpikeDefinition ceiling)
        {
            var floor=new RoomSpike {id="s",definition=spikes.id,x=0,y=0,width=.8f,height=.8f,danger=2};
            var top=new RoomSpike {id="c",definition=ceiling.id,x=0,y=2,width=.8f,height=.4f,danger=0};
            var pony=new ActorState {id="p",player=true,connected=true,level="l",x=.4f,y=.3f,vy=-6,health=100,actorType="pony"};
            // massa 1, dy = 6 m/s = 10 px per frame: 1·10/20·50·(1+0.2) = 30.
            Require(Mathf.Abs(SpikeRules.Damage(floor,spikes,pony)-30)<.001f,"урон падения massa·dy/20·урон·(1+0,1·опасность)");
            pony.vy=-4.7f;Require(SpikeRules.Damage(floor,spikes,pony)==0,"медленнее 8 px за кадр шипы не ранят");
            pony.vy=-6;pony.x=.9f;Require(SpikeRules.Damage(floor,spikes,pony)==0,"центр вне ящика по x");
            pony.x=.4f;pony.y=.9f;Require(SpikeRules.Damage(floor,spikes,pony)==0,"ноги выше ящика");
            pony.vy=3;pony.y=2.1f-Anatomy.Movement(pony).height;
            Require(Mathf.Abs(SpikeRules.Damage(top,ceiling,pony)-15)<.001f,"удар головой вверх: massa·урон");
            pony.vy=-3;Require(SpikeRules.Damage(top,ceiling,pony)==0,"потолочные шипы не ранят при падении");
            var rat=new ActorState {id="r",level="l",health=10,actorType="rat"};
            var bloat=new ActorState {id="b",level="l",health=10,actorType="bloat0"};
            Require(SpikeRules.Affects(pony) && SpikeRules.Affects(rat) && !SpikeRules.Affects(bloat),"мелкие существа ранятся, летуны нет (Trap.step/attKorp)");
            rat.health=0;Require(!SpikeRules.Affects(rat),"погибшие не ранятся");
            Require(Mathf.Approximately(SpikeRules.Cooldown(pony),1) && Mathf.Approximately(SpikeRules.Cooldown(rat),20f/30),"neujazMax: игрок 30 кадров, юнит 20");

            // Typed protection applies to the contact as to an attack of its type; the contact ID is received once.
            var receiver=new DamageReceiver(new PrototypeDamageRule(),null,new TypeHitProtectionRule());
            var target=new ActorState {id="t",level="l",health=100,actorType="raider"};
            float expected=new TypeHitProtectionRule().ResolveContact(target,DamageTypes.Blade,30,"x");
            Require(receiver.ApplyContact(target,"spikes:1",DamageTypes.Blade,30) && Mathf.Abs(target.health-(100-expected))<.001f,"урон шипов через защиту по типу «лезвие»");
            Require(!receiver.ApplyContact(target,"spikes:1",DamageTypes.Blade,30),"тот же контакт не наносится дважды");
            bool rejected=false;
            try {receiver.ApplyContact(target,"spikes:2","no-such-type",1);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"неизвестный тип урона контакта отклонён");
        }

        private static void CheckFactory()
        {
            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,GameAssets.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var room=FactoryContentSources.Require(world,"factory:0");
                var root=room.GetComponent<FactoryLevelRoot>();
                var placed=root.Objects.Where(o=>o.Source.Type.StartsWith("remains:obj:",StringComparison.Ordinal) &&
                    SpikeCatalog.ForObject(o.Source.Type.Substring("remains:obj:".Length))!=null).ToArray();
                var layout=room.CreateLayout(false);
                Require(placed.Length>0 && layout.spikes.Length==placed.Length,$"шипов в геометрии {layout.spikes.Length} из {placed.Length} в шаблонах");
                Require(layout.spikes.Select(s=>s.id).Distinct(StringComparer.Ordinal).Count()==layout.spikes.Length,"повтор ID шипов");
                foreach(var source in placed)
                {
                    var definition=SpikeCatalog.ForObject(source.Source.Type.Substring("remains:obj:".Length));
                    var spike=layout.spikes.Single(s=>s.id==$"{source.Room.Cell.X},{source.Room.Cell.Y}:{source.Source.Id}");
                    float cell=(float)source.Room.Template.Cell,bottom=definition.bottomAnchored?source.Position.y:source.Position.y+cell-definition.height;
                    Require(spike.definition==definition.id && Mathf.Abs(spike.x+spike.width*.5f-source.Position.x)<.001f && Mathf.Abs(spike.y-bottom)<.001f &&
                        Mathf.Approximately(spike.danger,source.Room.Cell.Y/2f),"ящик шипов на клетке шаблона: "+spike.id);
                }
                var near=new System.Collections.Generic.List<RoomSpike>();var sample=layout.spikes[layout.spikes.Length/2];
                SpikeRules.Near(layout,sample.x+sample.width*.5f,near);
                Require(near.Contains(sample) && near.All(s=>s.x<=sample.x+sample.width*.5f && sample.x+sample.width*.5f<=s.x+s.width),"поиск шипов по столбцу");
                Debug.Log($"Шипы seed104: {layout.spikes.Length} (на полу {layout.spikes.Count(s=>s.definition=="spikes")}, потолочных {layout.spikes.Count(s=>s.definition=="fspikes")}).");
            }
            finally {FactoryContentSources.Clear();}
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Шипы: "+message);}
    }
}
