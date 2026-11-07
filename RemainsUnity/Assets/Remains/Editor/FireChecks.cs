using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // FireRules: the molotov and the incendiary grenade from the original's data; a bottle breaks by its speed into a
    // surface (never below 3 m/s, surely from 6); a bottle's droplets splash along its flight and stop at a wall, a
    // grenade's fly out evenly; the same patches on every run; a torch burns all its droplets' time; the host's fire
    // from a grenade and its expiry; an actor touches a patch only when standing in it.
    public static class FireChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();ActorTypesLoad();
            CheckWick();
            CheckDropletPaths();
            CheckSurfaceAttachments();
            CheckFireLoopClip();
            var molotov=WeaponCatalog.Get("item:molotov");var incendiary=WeaponCatalog.Get("item:fgren");
            Require(molotov.IsFire && incendiary.IsFire && molotov.fire.shatter && !molotov.fire.radial && incendiary.fire.radial && !incendiary.fire.shatter,
                "коктейль — бутылка с инерцией, зажигательная — граната во все стороны");
            Require(molotov.fire.pulses==10 && Mathf.Approximately(molotov.fire.pulseDamage,20) && Mathf.Approximately(molotov.fire.igniteDamage,10) &&
                Mathf.Approximately(incendiary.fire.pulseDamage,30) && Mathf.Approximately(incendiary.fire.igniteDamage,15) && Mathf.Approximately(molotov.fire.igniteSeconds,7),
                "числа оригинала: 10 вспышек, 20/30 урона, поджог 10/15 в секунду 7 с");

            int some=0;
            for(int i=0;i<40;i++)
            {
                Require(!FireRules.Shatters(molotov.fire,2.9f,"b",i) && FireRules.Shatters(molotov.fire,6.1f,"b",i),"медленнее 3 м/с цела, от 6 м/с бьётся");
                if(FireRules.Shatters(molotov.fire,4.5f,"b",i)) some++;
            }
            Require(some>5 && some<35,$"между 3 и 6 м/с — по шансу: {some} из 40");

            var room=Room();
            float full=FireRules.Fuel(molotov.fire);
            Require(Mathf.Approximately(full,50) && Mathf.Approximately(molotov.grenadeFuseSeconds,full) && FireRules.Droplets(molotov.fire,full)==15 &&
                FireRules.Droplets(molotov.fire,20)==6 && FireRules.Droplets(molotov.fire,0)==0,"бутылка горит с броска: 50 с топлива; капель — по оставшемуся топливу");
            var bottle=new FireState {id="m",weapon="item:molotov",level="l",x=0,y=0,vx=9,vy=-3,seed=11,fuel=full};
            var splash=FireRules.Patches(bottle,room);
            Require(splash.Count>=10 && splash.Average(p=>p.position.x)>.5f && splash.All(p=>p.position.x<4),
                $"брызги бутылки — по ходу полёта и не за стену: {splash.Count} пятен, в среднем x={splash.Average(p=>p.position.x):0.00}");
            Require(FireRules.Patches(bottle,room).Select(p=>p.position).SequenceEqual(splash.Select(p=>p.position)),"одни и те же пятна при каждом расчёте");
            var burst=FireRules.Patches(new FireState {id="g",weapon="item:fgren",level="l",x=-6,y=0,seed=12,fuel=FireRules.Fuel(incendiary.fire)},room);
            Require(burst.Count>=10 && burst.Any(p=>p.position.x<-6.3f) && burst.Any(p=>p.position.x>-5.7f) && Mathf.Abs(burst.Average(p=>p.position.x)+6)<1.2f,
                "граната разбрызгивает во все стороны");

            // At the factory's coordinates droplets land on the floor, not in the air (rounding, PT-20261103-08).
            const float Far=395;var farRoom=Room(Far);
            var farSplash=FireRules.Patches(new FireState {id="f",weapon="item:molotov",level="l",x=-8,y=Far+1.5f,vx=7,vy=0,seed=13,fuel=full},farRoom);
            Require(farSplash.Count>=10 && farSplash.All(p=>Mathf.Abs(p.position.y-Far)<.01f),
                $"на координатах завода капли ложатся на пол: {farSplash.Count} пятен, высоты {string.Join(" ",farSplash.Select(p=>(p.position.y-Far).ToString("0.00")))}");

            var torch=new FireState {id="t",weapon="item:molotov",level="l",x=1,y=0,torch=true,start=10,fuel=full};
            Require(Math.Abs(FireRules.Duration(torch)-full)<.01 && FireRules.Patches(torch,room).Count==1,"горелка — одно пятно на всё оставшееся топливо");
            Require(FireRules.Patches(new FireState {id="m",weapon="item:molotov",level="l",x=0,y=0,vx=9,vy=-3,seed=11,fuel=20},room).Count<=6,"догоревшая бутылка разбрызгивает меньше");
            Require(FireRules.Pulse(torch,10.1)==0 && FireRules.Pulse(torch,10.4)==1 && FireRules.Pulse(torch,9)==-1 && FireRules.Pulse(torch,61)==-1,"вспышки раз в треть секунды, вне огня нет");

            var near=new ActorState {id="a",actorType="pony",anatomy="pony",x=splash[0].position.x,y=splash[0].position.y,level="l"};
            var far=new ActorState {id="b",actorType="pony",anatomy="pony",x=splash[0].position.x+3,y=splash[0].position.y+2,level="l"};
            Require(FireRules.Touches(splash[0],near) && !FireRules.Touches(splash[0],far),"обжигает стоящего в пятне, не стоящего в стороне");

            // The bottle in flight: hard into the floor it breaks with the velocity it hit; a soft drop stays whole.
            var hard=new GrenadeState {id="hard",weapon="item:molotov",x=0,y=1,vx=6,vy=-8,remaining=molotov.grenadeFuseSeconds,rotationVersion=1};
            GrenadeRules.Step(hard,room,.5f);
            Require(hard.simulationRemainder==0,"разбитая бутылка не оставляет остатка часов: хост отклонит её конец");
            Require(hard.shattered && hard.remaining==0 && hard.impactVy<-6 && hard.fuel>full-1 && hard.fuel<full,"сильный удар разбивает бутылку, топливо почти всё");
            var soft=new GrenadeState {id="soft",weapon="item:molotov",x=0,y=.15f,vx=.5f,vy=0,remaining=molotov.grenadeFuseSeconds,rotationVersion=1};
            GrenadeRules.Step(soft,room,3);
            Require(soft.simulationRemainder==0,"упавшая бутылка не оставляет остатка часов");
            Require(!soft.shattered && soft.remaining==0 && soft.fuel>full-3 && soft.fuel<full,$"мягко упавшая бутылка цела и, остановившись, становится горелкой: топливо {soft.fuel:0.0}");

            var world=new WorldState();world.grenades.Add(soft);world.grenades.Add(hard);
            var lit=FireRules.Ignite(world,soft,3);var broke=FireRules.Ignite(world,hard,3);
            Require(lit.torch && !broke.torch && Mathf.Approximately(lit.fuel,soft.fuel) && world.grenades.Count==0 && world.fires.Count==2 && Mathf.Approximately(broke.vy,hard.impactVy),
                "целая — горелка с остатком топлива, разбитая — брызги со скоростью удара");
            Require(!FireRules.Expire(world,_=>4) && FireRules.Expire(world,_=>3+FireRules.Duration(broke)+.01) && world.fires.Count==1 && world.fires[0]==lit,"догоревший огонь убирается");
            Debug.Log($"Огонь: PASS — оригинальные числа, порог разбивания, брызги бутылки {splash.Count} по ходу и до стены, граната во все стороны, повторяемость, горелка, вспышки, касание, хост и догорание.");
        }
        private static void CheckDropletPaths()
        {
            foreach(string weapon in new[]{"item:molotov","item:fgren"})
            {
                var room=Room();
                room.platforms=Array.FindAll(room.platforms,surface=>surface.x!=4);
                RoomSurfaces.Prepare(room);
                var profile=WeaponCatalog.Get(weapon).fire;
                var grenade=new GrenadeState {id="timed-"+weapon,weapon=weapon,level=room.id,x=0,y=1.7f,
                    shattered=profile.shatter,impactVx=8,impactVy=-6,fuel=FireRules.Fuel(profile)};
                var world=new WorldState {worldId="droplets",grenades={grenade}};
                var fire=FireRules.Ignite(world,grenade,10,room);
                Require(fire.dropletPaths.Length==profile.droplets && fire.dropletPaths.All(path=>path.TryPosition(.05,out _)),
                    "план не показывает все капли в начале полёта");
                var received=StateJson.Copy(world).fires.Single();
                // A peer may have a later door state. It still draws and applies the host's contacts.
                var patches=FireRules.Patches(received,null);
                Require(patches.Count>=10 && patches.All(patch=>!patch.IsBurning(.05,profile.PatchSeconds)),
                    "очаг загорелся до прилёта капли");
                var legacy=StateJson.Copy(received);legacy.dropletPaths=null;
                Require(patches.Select(patch=>patch.position).SequenceEqual(FireRules.Patches(legacy,room).Select(patch=>patch.position)),
                    "время полёта изменило рассчитанные точки попадания");
                int index=0;
                foreach(var path in received.dropletPaths)
                {
                    if(!path.landed) continue;
                    var patch=patches[index++];
                    Require(path.TryPosition(path.Arrival-.0001,out var near) && Vector2.Distance(near,patch.position)<.005f &&
                        !path.TryPosition(path.Arrival,out _) && patch.IsBurning(path.Arrival,profile.PatchSeconds),
                        "пламя и очаг разошлись в момент попадания");
                    Require(!patch.IsBurning(path.Arrival+profile.PatchSeconds,profile.PatchSeconds),
                        "очаг не погас по окончании своего времени горения");
                }
                Require(FireRules.Duration(received)>profile.PatchSeconds &&
                    StateJson.ToJson(received).Length<6000,"план не продлил последнее пятно или содержит лишние данные");
            }
            Debug.Log("Полёт горящих капель: PASS — Молотов и граната, план хоста, точный переход в очаг и восстановление.");
        }

        private static void CheckSurfaceAttachments()
        {
            foreach(float origin in new[]{0f,395f})
            {
                var room=new RoomLayout {id="surface-fire",left=origin-4,right=origin+4,bottom=origin-2,top=origin+4,
                    platforms=new[]{new RoomSurface {x=origin-4,y=origin-1,width=8,height=1},
                        new RoomSurface {x=origin-.85f,y=origin,width=.2f,height=1.25f},
                        new RoomSurface {x=origin+.65f,y=origin,width=.2f,height=1.25f},
                        new RoomSurface {x=origin-1,y=origin+1.25f,width=2,height=.2f}}};
                RoomSurfaces.Prepare(room);
                var seen=new HashSet<FireAttachment>();
                for(int seed=0;seed<6;seed++)
                {
                    var grenade=new GrenadeState {id="surface-"+seed,weapon="item:fgren",level=room.id,x=origin,y=origin+.8f};
                    var world=new WorldState {grenades={grenade}};
                    var fire=StateJson.Copy(FireRules.Ignite(world,grenade,10,room));
                    foreach(var patch in FireRules.Patches(fire,null))
                    {
                        seen.Add(patch.attachment);
                        float plane=patch.attachment switch
                        {
                            FireAttachment.WallLeft=>patch.position.x-(origin+.65f),
                            FireAttachment.WallRight=>patch.position.x-(origin-.65f),
                            FireAttachment.Ceiling=>patch.position.y-(origin+1.25f),
                            _=>patch.position.y-origin
                        };
                        Require(Mathf.Abs(plane)<.001f,"очаг не прикреплён к плоскости контакта");
                        if(patch.attachment==FireAttachment.Floor) continue;
                        foreach(float edge in new[]{-10f,10f})
                        {
                            var onSurface=patch.OffsetAlongSurface(edge);
                            Require(patch.attachment==FireAttachment.Ceiling
                                    ? onSurface.x>=origin-1.001f && onSurface.x<=origin+1.001f
                                    : onSurface.y>=origin-.001f && onSurface.y<=origin+1.251f,
                                "пламя вышло за край несущей поверхности");
                        }
                        var lights=new FireLightSources();lights.Begin();lights.Fire(fire,new[]{patch},1);
                        var sample=lights.Emissions.Single().Samples.Single();
                        Require(Vector2.Distance(new Vector2(sample.x,sample.y),patch.position+patch.Normal*.22f)<.001f,
                            "свет направлен в стену или потолок");
                        var actor=new ActorState {id="surface-target",anatomy="pony"};
                        var hull=Anatomy.Movement(actor);
                        var center=Vector2.up*hull.height*.5f;
                        float extent=Mathf.Abs(Vector2.Dot(patch.Normal,new Vector2(hull.width,hull.height)*.5f));
                        actor.Position=patch.position+patch.Normal*(extent+.2f)-center;
                        Require(FireRules.Touches(patch,actor),"очаг на поверхности не обжигает рядом");
                        actor.Position=patch.position-patch.Normal*(extent+.2f)-center;
                        Require(!FireRules.Touches(patch,actor),"очаг обжигает за преградой");
                        actor.Position=patch.position+patch.Normal*(extent+FireRules.Reach+.1f)-center;
                        Require(!FireRules.Touches(patch,actor),"очаг обжигает за пределом досягаемости");
                        float width=Mathf.Abs(Vector2.Dot(patch.Tangent,new Vector2(hull.width,hull.height)*.5f));
                        actor.Position=patch.OffsetAlongSurface(10)+patch.Tangent*(width+.1f)+patch.Normal*(extent+.2f)-center;
                        Require(!FireRules.Touches(patch,actor),"очаг обжигает за концом поверхности");
                    }
                }
                Require(seen.Count==4,$"Нет контакта с одной из поверхностей, координаты {origin:R}");
                room.left=origin-.65f;room.right=origin+.65f;
                room.platforms=new[]{room.platforms[0],new RoomSurface {x=origin-1,y=origin+1.25f,width=2,height=.2f,oneWay=true}};
                room.platforms=DoorGeometry.Compose(room.platforms,new[]{new DoorState {id="moving-door",definition="fixture",
                    level=room.id,surfaceTexture="wall",x=origin+.32f,y=origin,width=.18f,height=1.25f}},room.id);
                RoomSurfaces.Prepare(room);
                var loose=new GrenadeState {id="surface-0",weapon="item:fgren",level=room.id,x=origin,y=origin+.8f};
                var looseWorld=new WorldState {grenades={loose}};
                var looseFire=FireRules.Ignite(looseWorld,loose,10,room);
                Require(FireRules.Patches(looseFire,room).All(patch=>patch.attachment==FireAttachment.Floor),
                    "дверь, односторонняя поверхность или невидимая граница стали стеной горения");
            }
            Debug.Log("Очаги на поверхностях: PASS — обе стены, потолок, пол, свет, урон и координаты завода.");
        }

        private static void CheckFireLoopClip()
        {
            var clip=GameAssets.Load<AudioClip>("Presentation/fire-crackle");
            Require(clip!=null && clip.channels==1 && Mathf.Abs(clip.length-3.2f)<.01f,"петля горения должна быть моно длиной 3,2 с");
            var data=new float[clip.samples];
            Require(clip.GetData(data,0) && data.Max(sample=>Mathf.Abs(sample))>.1f &&
                Mathf.Abs(data[0]-data[^1])<.025f,"петля горения пуста или имеет скачок на стыке");
        }

        private static void CheckWick()
        {
            var prefab=GrenadeArtwork.LoadPrefab("item:molotov");
            var offset=GrenadeArtwork.WickOffset(prefab);
            Require(Vector2.Distance(offset,new Vector2(.032f,.433f))<.0001f,
                "фитиль не совпал с исходным пикселем горлышка");
            var origin=new Vector2(4,5);
            foreach(var (angle,mirrored,expected) in new[]{
                (0f,false,new Vector2(.032f,.433f)),(90f,false,new Vector2(-.433f,.032f)),
                (180f,false,new Vector2(-.032f,-.433f)),(270f,false,new Vector2(.433f,-.032f)),
                (0f,true,new Vector2(.032f,-.433f)),(90f,true,new Vector2(.433f,.032f))})
                Require(Vector2.Distance(GrenadeArtwork.WickPosition(origin,angle,mirrored,offset),origin+expected)<.0001f,
                    $"поворот или отражение переместили фитиль, angle={angle}, mirrored={mirrored}");
            Debug.Log("Фитиль Молотова: PASS — исходный кадр, четыре угла и отражение.");
        }

        private static void ActorTypesLoad() {ActorTypes.Load();Anatomy.Load();}
        private static RoomLayout Room(float floor=0)
        {
            var room=new RoomLayout {id="fire-check",left=-20,right=20,bottom=floor-1,top=floor+10,ladders=Array.Empty<RoomLadder>(),platforms=new[]{
                new RoomSurface {x=-20,y=floor-1,width=40,height=1},new RoomSurface {x=4,y=floor,width=.5f,height=6},
                new RoomSurface {x=-21,y=floor-1,width=1,height=12},new RoomSurface {x=20,y=floor-1,width=1,height=12}}};
            RoomSurfaces.Prepare(room);return room;
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Огонь: "+message);}
    }
}
