using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class MeleeImpactAudioChecks
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void Run()
        {
            var corner=new RoomSurface {x=0,y=0,width=1,height=1};
            Require(!SurfaceContact.Entry(corner,new Vector2(-.09f,-.09f),new Vector2(-.08f,-.08f),.1f).HasValue,
                "Квадратное расширение создало ложный контакт за округлым углом.");
            Require(SurfaceContact.Entry(corner,new Vector2(-.06f,-.06f),new Vector2(-.06f,-.06f),.1f)==0,
                "Начальное перекрытие округлого угла потеряно.");
            var grazing=SurfaceContact.Entry(corner,new Vector2(.5f,1.5f),new Vector2(.5f,1.05f),.1f);
            Require(grazing.HasValue && Mathf.Abs(grazing.Value-8f/9)<.00001f,
                "Радиус не остановил касание верхнего ребра в правильной точке.");
            var root=new GameObject("Проверка звуков контакта ближнего оружия");
            try
            {
                var session=root.AddComponent<CoopSession>();
                var world=new WorldState();
                var observer=new ActorState {id="source",owner="remote",player=true,connected=true,level="hub",x=-3};
                var target=new ActorState {id="target",owner="remote",actorType="pony",level="hub",x=2};
                world.actors.Add(observer);world.actors.Add(target);
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                typeof(CoopSession).GetProperty("LocalActorId").SetValue(session,observer.id);
                var geometry=new SimulationGeometry(_=>new RoomLayout {platforms=Array.Empty<RoomSurface>()});
                geometry.Reconcile(world);
                var combat=new CombatController(session,geometry);
                var playback=root.AddComponent<GameplayAudio>();playback.Initialize(session,combat.Reload,root.AddComponent<Camera>());
                var audio=root.AddComponent<MeleeImpactAudio>();audio.Initialize(combat,playback);
                var receive=typeof(CombatController).GetMethod("ReceiveAttack",Private);
                int expected=0;
                foreach(string weapon in new[]{"crowbar","spear"})
                foreach(string type in new[]{"pony","raider","training-dummy","training-armored-dummy"})
                {
                    target.actorType=type;
                    var attack=new AttackEvent {id=weapon+type,attacker=observer.id,sourceOwner="remote",sourcePlayer=true,
                        weapon=weapon,level="hub",originY=.7f,endX=4,endY=.7f,radius=.08f};
                    receive.Invoke(combat,new object[]{attack});receive.Invoke(combat,new object[]{attack});
                    expected++;
                    Require(audio.Played==expected,"Контакт пропущен или повторён: "+attack.id);
                    var source=root.GetComponentsInChildren<AudioSource>().Last();
                    var clip=Resources.Load<AudioClip>("Original/Audio/"+(type=="training-armored-dummy"?"hit-metal":weapon=="spear"?"hit-blade":"hit-flesh"));
                    Require(source.clip==clip,"Неверный WAV: "+attack.id);
                    float gain=type=="training-armored-dummy"?.4f:weapon=="spear"?.8f:.5f;
                    Require(Mathf.Abs(source.volume-playback.Volume*gain*GameplayAudio.Attenuation(Vector2.Distance(attack.End,observer.Position)))<.00001f,"Неверная громкость: "+attack.id);
                    Require(!Application.isBatchMode || source.mute,"Фоновый звук не заглушен.");
                    target.id+="-next";
                    receive.Invoke(combat,new object[]{attack});expected++;
                    Require(audio.Played==expected,"Вторая цель одного взмаха ошибочно заглушена.");
                }
                var miss=new AttackEvent {id="miss",attacker=observer.id,sourceOwner="remote",sourcePlayer=true,
                    weapon="crowbar",level="hub",originY=20,endX=4,endY=20};
                receive.Invoke(combat,new object[]{miss});
                Require(audio.Played==expected,"Промах создал звук контакта.");
                world.friendlyFire=false;target.player=true;target.connected=true;
                miss.id="friendly-disabled";miss.originY=miss.endY=.7f;
                receive.Invoke(combat,new object[]{miss});
                Require(audio.Played==expected,"Выключенный дружественный огонь создал контакт с союзником.");
                target.x=100;
                foreach(string weapon in new[]{"crowbar","spear"})
                foreach(string texture in new[]{"floor","Hub/concrete","Factory46/26_tConcrete","Factory18/26_tConcrete"})
                {
                    geometry.Get("hub").platforms=new[]{new RoomSurface {x=1,y=0,width=.2f,height=2,texture=texture}};
                    var wall=new AttackEvent {id="wall-"+weapon+texture,attacker=observer.id,sourceOwner="remote",
                        weapon=weapon,level="hub",originY=.7f,endX=4,endY=.7f};
                    receive.Invoke(combat,new object[]{wall});receive.Invoke(combat,new object[]{wall});expected++;
                    Require(audio.Played==expected,"Контакт стены потерян или повторён: "+wall.id);
                    var source=root.GetComponentsInChildren<AudioSource>().Last();
                    string clip=texture=="floor"?"hit-metal":"hit-concrete";
                    float gain=texture=="floor"?.4f:.5f;
                    Require(source.clip==Resources.Load<AudioClip>("Original/Audio/"+clip),"Неверный материал звука стены.");
                    float attenuation=GameplayAudio.Attenuation(Vector2.Distance(new Vector2(1,.7f),observer.Position));
                    Require(Mathf.Abs(source.volume-playback.Volume*gain*attenuation)<.00001f,"Звук стены использует конец взмаха вместо точки контакта.");
                    wall.id+="-miss";wall.originY=wall.endY=3;
                    receive.Invoke(combat,new object[]{wall});Require(audio.Played==expected,"Промах над стеной озвучен как контакт.");
                    wall.id+="-edge";wall.originY=wall.endY=2.05f;wall.radius=.08f;
                    receive.Invoke(combat,new object[]{wall});receive.Invoke(combat,new object[]{wall});expected++;
                    Require(audio.Played==expected,"Касание стены краем оружия пропущено или озвучено дважды.");
                    wall.id+="-outside";wall.originY=wall.endY=2.1f;
                    receive.Invoke(combat,new object[]{wall});
                    Require(audio.Played==expected,"Радиус оружия создал звук за пределами досягаемости.");
                }
                geometry.Get("hub").platforms=Array.Empty<RoomSurface>();
                world.props.Add(new PropState {id="wood",definition="box",level="hub",x=1,y=.7f});
                world.containers.Add(new ContainerState {id="metal",definition="mcrate1",level="hub",x=2,y=0});
                world.props.Add(new PropState {id="other-level",definition="box",level="factory:0",x=1,y=.7f});
                string beforeProps=Remains.Serialization.StateJson.ToJson(world);
                foreach(string weapon in new[]{"crowbar","spear"})
                {
                    var strike=new AttackEvent {id="props-"+weapon,attacker=observer.id,sourceOwner="remote",
                        weapon=weapon,level="hub",originY=.7f,endX=4,endY=.7f,radius=.08f};
                    int beforePlayed=audio.PropPlayed;
                    receive.Invoke(combat,new object[]{strike});receive.Invoke(combat,new object[]{strike});
                    Require(audio.PropPlayed==beforePlayed+2,"Объект/контейнер пропущен, повторён или озвучен в другом уровне.");
                    Require(root.GetComponentsInChildren<AudioSource>().Any(s=>s.clip==Resources.Load<AudioClip>("Original/Audio/hit-wood")),
                        "Деревянный ящик не использует исходный звук дерева.");
                    strike.id+="-miss";strike.originY=strike.endY=4;
                    receive.Invoke(combat,new object[]{strike});
                    Require(audio.PropPlayed==beforePlayed+2,"Промах мимо объектов создал контакт.");
                }
                Require(Remains.Serialization.StateJson.ToJson(world)==beforeProps,"Косметический контакт изменил мир или содержимое контейнера.");
                // Use registered factory dimensions and definitions, rather than legacy hub-only fixtures.
                PropCatalog.Load();world.props.Clear();world.containers.Clear();
                foreach(string definition in new[]{"woodbox","chest","ammobox","bigbox","mcrate4"})
                foreach(string weapon in new[]{"crowbar","spear"})
                {
                    var prop=PropCatalog.Create("factory-contact",definition,"hub",new Vector2(2,1));
                    world.props.Add(prop);
                    var strike=new AttackEvent {id="factory-"+weapon+definition,attacker=observer.id,sourceOwner="remote",
                        weapon=weapon,level="hub",originY=1,endX=4,endY=1,radius=.08f};
                    int beforePlayed=audio.PropPlayed;
                    string before=Remains.Serialization.StateJson.ToJson(prop);
                    receive.Invoke(combat,new object[]{strike});receive.Invoke(combat,new object[]{strike});
                    Require(audio.PropPlayed==beforePlayed+1,"Заводской объект пропущен или озвучен дважды: "+strike.id);
                    var source=root.GetComponentsInChildren<AudioSource>().Last();
                    bool wood=definition=="woodbox";
                    Require(source.clip==Resources.Load<AudioClip>("Original/Audio/"+(wood?"hit-wood":"hit-metal")),"Неверный материал заводского объекта: "+definition);
                    float gain=wood?.5f:.4f;
                    var point=new Vector2(prop.x-prop.width*.5f,1);
                    Require(Mathf.Abs(source.volume-playback.Volume*gain*GameplayAudio.Attenuation(Vector2.Distance(point,observer.Position)))<.00001f,
                        "Неверная громкость заводского объекта: "+definition);
                    Require(Remains.Serialization.StateJson.ToJson(prop)==before,"Звук изменил заводской объект: "+definition);
                    world.props.Clear();
                }
                Debug.Log("Контакты ближнего оружия: PASS — актёры, стены, край оружия, дерево/металл объектов и контейнеров, уровни, дедупликация и сохранность мира.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Проверка звука ближнего оружия: "+reason);}
    }
}
