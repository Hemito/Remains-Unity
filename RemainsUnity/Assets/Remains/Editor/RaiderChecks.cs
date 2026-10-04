using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class RaiderChecks
    {
        public static void Run()
        {
            var world=new WorldState();
            var actor=new ActorState {id="npc:training-guard",x=6,y=2,health=37,down=true,owner="client-b",generation=7,attackId="saved"};
            actor.inventory.Add(new ItemStack {id="saved-item",definition="heal",count=2});world.actors.Add(actor);
            FactoryRaiderContent.Install(world);long revision=world.revision;FactoryRaiderContent.Install(world);
            Require(actor.actorType=="raider" && actor.anatomy=="raider" && actor.health==37 && actor.down && actor.x==6 && actor.y==2 && actor.owner=="client-b" &&
                actor.generation==7 && actor.attackId=="saved" && actor.inventory[0].count==2 && world.revision==revision,"Замена графики изменила состояние NPC");
            var customWorld=new WorldState();customWorld.actors.Add(new ActorState {id="npc:training-guard",actorType="raider",anatomy="custom"});
            FactoryRaiderContent.Install(customWorld);Require(customWorld.actors[0].anatomy=="custom","Миграция заменила анатомию мода");
            var copy=world.Copy();Require(copy.Find(actor.id).actorType=="raider" && !FactoryRaiderContent.NeedsInstall(copy),"Сохранение потеряло тип рейдера");
            var visual=(AtlasActorVisual)ActorVisualFactory.Create(actor);
            try
            {
                actor.down=false;visual.Bind(actor);var renderer=visual.GetComponent<SpriteRenderer>();
                Require(EntityAssetCatalog.Load().TryGetActorPrefab(ActorTypes.Get("raider"),out var source),"Нет исходного префаба рейдера");
                var sourceRenderer=source.GetComponent<SpriteRenderer>();var sourceSprite=sourceRenderer.sprite;
                foreach(string pose in new[]{"idle","walk","run","jump","climb","down"})
                {
                    actor.pose=pose;actor.poseTime=0;actor.vy=pose=="jump"?7.2f:0;visual.Draw();var first=renderer.sprite;
                    actor.poseTime=.1f;actor.vy=0;visual.Draw();
                    Require(renderer.enabled && renderer.sprite!=null && renderer.sprite.rect.width==120,"Не отображается поза рейдера: "+pose);
                    Require(pose=="idle" || first!=renderer.sprite,"Не меняется кадр: "+pose);
                    var clip=AtlasAnimationData.Get("Original/Raider/artwork").Clip(pose);
                    for(int frame=0;frame<clip.count*3;frame++)
                    {
                        actor.poseTime=(frame+.25f)/clip.framesPerSecond;actor.facing=1;visual.Draw();
                        if(pose=="jump")
                        {
                            int index=frame%16;
                            actor.vy=index<=7?7.2f*(1-index/7f):-7.2f*(index-7)/8f;visual.Draw();
                            Require(Mathf.Abs(renderer.sprite.rect.x/120-index)<.001f,"Воздушный кадр не соответствует скорости рейдера");
                        }
                        if(pose=="walk")
                            Require(clip.FrameIndex(actor.poseTime)==(frame==0?0:1+(frame-1)%16),"В повтор ходьбы рейдера попал вступительный кадр");
                        Require(Mathf.Abs(renderer.sprite.rect.x/120-clip.Sample(actor))<.001f,"Кадр рисунка расходится с выбором кадра анатомии");
                        foreach(string region in new[]{"head","torso","legs"})
                        {
                            Require(Anatomy.TryRegion(actor,region,out var center,out float radius),"Нет области кадра: "+pose+":"+frame+":"+region);
                            var hit=new AttackEvent {weapon="pistol",originX=center.x,endX=center.x,originY=center.y,endY=center.y};
                            Require(Anatomy.Contact(actor,hit)!=null,"Выстрел в центр области не попадает");
                            actor.facing=-1;Require(Anatomy.TryRegion(actor,region,out var mirrored,out _),"Нет отражённой области");
                            Require(Mathf.Abs(center.x+mirrored.x-2*actor.x)<.0001f && Mathf.Abs(center.y-mirrored.y)<.0001f,"Область не отражается вместе с рисунком");
                            actor.facing=1;
                        }
                    }
                }
                CheckAirborne(actor,visual,renderer);
                actor.pose="down";actor.poseTime=10;visual.Draw();var final=renderer.sprite;
                actor.poseTime=11;visual.Draw();Require(renderer.sprite==final,"Падение зациклено");
                actor.facing=-1;visual.Draw();Require(renderer.flipX,"Не отражается рейдер влево");
                actor.facing=1;visual.Draw();Require(!renderer.flipX,"Не отражается рейдер вправо");
                Require(renderer.sharedMaterial==sourceRenderer.sharedMaterial && sourceRenderer.sprite==sourceSprite && source.transform.position==Vector3.zero,
                    "Рабочая анимация заменила материал префаба или изменила исходный asset");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
            var target=new ActorState {id="target",player=true,connected=true,x=-3,y=4};
            NpcAttackRules.Begin(actor,target);var shot=NpcAttackRules.Complete(actor,target);
            var muzzle=WeaponPose.Muzzle(actor);
            Require(shot!=null && Vector2.Distance(new Vector2(shot.originX,shot.originY),muzzle)<.0001f,"Выстрел NPC не начинается из дула");
            Require(Mathf.Abs(Vector3.Cross(new Vector2(shot.endX,shot.endY)-muzzle,WeaponPose.Direction(actor)).z)<.0001f,"Дуло NPC не направлено на цель");
            Debug.Log("Рейдер: PASS — кадры, направление, сохранение, повтор обновления, выстрел из дула.");
        }
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);WeaponCatalog.Load();Anatomy.Load();Run();
            string[] poses={"idle","walk","run","jump","climb","down"};
            for(int i=0;i<poses.Length;i++)
            {
                var actor=new ActorState {id="raider-"+poses[i],actorType="raider",anatomy="raider",pose=poses[i],poseTime=poses[i]=="down"?1:.1f,x=(i%3)*3,y=-(i/3)*3};
                ActorVisualFactory.Create(actor).Bind(actor);
                foreach(string region in new[]{"head","torso","legs"})
                {
                    if(!Anatomy.TryRegion(actor,region,out var center,out float radius)) throw new InvalidOperationException("Нет области для снимка: "+region);
                    var ring=new GameObject("Область "+region).AddComponent<LineRenderer>();
                    ring.material=new Material(Shader.Find("Sprites/Default"));ring.useWorldSpace=true;ring.loop=true;ring.positionCount=48;
                    ring.startWidth=ring.endWidth=.025f;ring.sortingOrder=100;
                    ring.startColor=ring.endColor=region=="head"?Color.red:region=="torso"?Color.yellow:Color.cyan;
                    for(int v=0;v<48;v++) {float angle=v*Mathf.PI*2/48;ring.SetPosition(v,new Vector3(center.x+Mathf.Cos(angle)*radius,center.y+Mathf.Sin(angle)*radius,-.1f));}
                }
            }
            var camera=new GameObject("Камера рейдера").AddComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=3.5f;camera.transform.position=new Vector3(3,-.4f,-10);camera.backgroundColor=new Color(.22f,.23f,.25f);
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/raider-regions.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));Remains.Runtime.NetworkFrameCapture.Save(camera,path);
            Debug.Log("Кадр рейдера: "+path);
        }
        private static void CheckAirborne(ActorState actor,AtlasActorVisual visual,SpriteRenderer renderer)
        {
            float[] velocities={-20,-7.2f,0,7.2f,20};int[] expected={15,15,7,0,0};
            actor.pose="jump";actor.down=false;actor.facing=1;
            for(int i=0;i<velocities.Length;i++)
            {
                actor.vy=velocities[i];actor.poseTime=0;visual.Draw();
                Require(Anatomy.TryRegion(actor,"head",out var before,out var radius),"Нет головы в воздушной позе");
                actor.owner="other-client";actor.generation++;actor.poseTime=100;visual.Draw();
                Require(Mathf.Abs(renderer.sprite.rect.x/120-expected[i])<.001f,"Воздушная поза зависит от времени/владельца");
                Require(Anatomy.TryRegion(actor,"head",out var after,out var afterRadius) && before==after && radius==afterRadius,
                    "Область попадания сменилась без изменения движения");
            }
            bool rejected=false;
            try {MotionFrameSampling.Validate(new[] {new MotionFramePoint {velocity=0},new MotionFramePoint {velocity=0}},16,"проверка");}
            catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Принята неоднозначная кривая воздушной позы");
        }
        public static void RenderAirborne()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);WeaponCatalog.Load();Anatomy.Load();Run();
            float[] velocities={7.2f,0,-7.2f};
            for(int row=0;row<2;row++)
            for(int column=0;column<3;column++)
            {
                var actor=new ActorState {id=$"airborne-{row}-{column}",actorType="raider",anatomy="raider",
                    pose="jump",poseTime=row*100,vy=velocities[column],x=column*3,y=-row*3,facing=1};
                ActorVisualFactory.Create(actor).Bind(actor);
            }
            var camera=new GameObject("Камера воздушных поз").AddComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=3.5f;camera.transform.position=new Vector3(3,-.4f,-10);camera.backgroundColor=new Color(.22f,.23f,.25f);
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/raider-airborne-motion.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));Remains.Runtime.NetworkFrameCapture.Save(camera,path,width:1920,height:1080);
            Debug.Log("Воздушные позы: верх t=0, низ t=100; слева направо vy=+7,2/0/−7,2. "+path);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException(reason);}
    }
}
