using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // The controller selects the stance on real surfaces. Artwork only displays
    // those same surfaces; it does not supply the support/contact result.
    public static class PonyHeadSlopeChecks
    {
        private const float DeltaTime=1f/60;
        private static readonly Rect Viewport=new(-2,-10,32,16);

        [Serializable] private sealed class Observation
        {
            public string headStyleId,pose;
            public int slopeDirection,facing,frame,footPartIndex;
            public Vector2 anchor,body,contact;
        }
        [Serializable] private sealed class Evidence
        {
            public int computedStances,nativeCaptures;
            public List<Observation> observations=new();
        }

        [MenuItem("Remains/Проверить формы головы на наклонах")]
        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();WeaponCatalog.Load();PonyVisual.Prepare();SlopePoseCatalog.Load();
            Require(SlopePoseCatalog.TryGet("pony",out var profile),"Нет явного профиля наклонных поз pony.");
            Require(PonyHeadCatalog.Styles.Count>0,"Каталог форм головы пуст.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/pony-heads"));
            Directory.CreateDirectory(directory);
            var evidence=new Evidence();
            foreach(int direction in new[]{-1,1}) foreach(int facing in new[]{-1,1})
                CheckCase(direction,facing,profile,directory,evidence);
            Require(evidence.computedStances==PonyHeadCatalog.Styles.Count*4 && evidence.nativeCaptures==4,
                $"Неполное покрытие: стоек={evidence.computedStances}, кадров={evidence.nativeCaptures}.");
            File.WriteAllText(Path.Combine(directory,"slope-stance-results.json"),JsonUtility.ToJson(evidence,true));
            Debug.Log($"Формы головы на наклонах: PASS — вычисленных стоек={evidence.computedStances}, нативных изображений={evidence.nativeCaptures}; оба наклона и направления взгляда, общий BodyPosition, исходные физика и sampled foot-support. Каталог: {directory}.");
        }

        private static void CheckCase(int direction,int facing,SlopePoseProfile profile,string directory,Evidence evidence)
        {
            var room=Fixture(direction);var objects=new List<GameObject>();var boundActors=new List<ActorState>();
            try
            {
                objects.Add(RoomVisual.Build(room,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()}));
                for(int i=0;i<PonyHeadCatalog.Styles.Count;i++)
                {
                    string head=PonyHeadCatalog.Styles[i].id;var surface=room.platforms[i];
                    var actor=new ActorState {id=$"head-slope:{head}:{direction}:{facing}",player=true,connected=true,
                        x=surface.x+surface.width*.5f,facing=facing,weapon=null,
                        appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId=head,
                            fur=0xBBCCDD,hair=0x8855DD,eyes=0x66DD55}};
                    actor.y=RoomSupport.Top(surface,actor.x,Anatomy.Movement(actor).width);
                    var original=StateJson.Copy(actor);original.id+="-original";
                    original.appearance.headStyleId=CharacterAppearance.OriginalHeadStyleId;
                    ActorBodySupport.Bind(actor,room);boundActors.Add(actor);
                    ActorBodySupport.Bind(original,room);boundActors.Add(original);
                    var motion=new MovementController(room);var originalMotion=new MovementController(room);
                    var initial=actor.Position;
                    for(int tick=0;tick<13;tick++)
                    {
                        if(tick>0) {ActorPose.Advance(actor,DeltaTime);ActorPose.Advance(original,DeltaTime);}
                        motion.Step(actor,Vector2.zero,false,DeltaTime);
                        originalMotion.Step(original,Vector2.zero,false,DeltaTime);
                        Require(actor.Position==initial && actor.Position==original.Position && actor.Velocity==Vector2.zero &&
                            actor.Velocity==original.Velocity && actor.facing==facing && actor.pose==original.pose &&
                            actor.poseTime==original.poseTime && actor.poseSequence==original.poseSequence,
                            $"Голова изменила неподвижную физику/фазу: head={head}, slope={direction}, facing={facing}, tick={tick}.");
                    }
                    string expectedPose=profile.Select(false,facing*direction>0);
                    Require(actor.pose==expectedPose && PonyMotionFrames.Sample(actor,actor.poseTime)==0,
                        $"Контроллер не выбрал наклонную стойку: head={head}, expected={expectedPose}, actual={actor.pose}.");
                    Require(ActorTerrainMotion.IsClear(room,Anatomy.Movement(actor),actor.Position) &&
                        ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),
                        $"У стойки нет физической опоры: head={head}, anchor={actor.Position}.");
                    var sample=ActorBodySupport.Sample(actor);var baseline=ActorBodySupport.Sample(original);
                    Require(sample.IsBound && sample.Grounded && sample.HasContact && sample.Outcome==ActorBodySupportOutcome.Contact &&
                        sample.SlopeDirection==direction && sample.Frame==0 && sample.Pose==expectedPose &&
                        sample.SurfaceBounds==new Rect(surface.x,surface.y,surface.width,surface.height),
                        $"Нет контакта с собственным наклоном: head={head}, outcome={sample.Outcome}, pose={sample.Pose}, frame={sample.Frame}.");
                    Require(sample.BodyPosition==baseline.BodyPosition && sample.Anchor==baseline.Anchor &&
                        sample.ContactPoint==baseline.ContactPoint && sample.PartIndex==baseline.PartIndex &&
                        sample.PixelX==baseline.PixelX && sample.PixelY==baseline.PixelY,
                        $"Форма головы изменила sampled foot-support: head={head}, body={sample.BodyPosition}, original={baseline.BodyPosition}.");
                    Require(Mathf.Abs(sample.ContactPoint.y-RoomSupport.Top(surface,sample.ContactPoint.x))<.00003f,
                        $"Контакт стопы находится вне поверхности: head={head}, contact={sample.ContactPoint}.");
                    var visual=(PonyVisual)ActorVisualFactory.Create(actor);objects.Add(visual.gameObject);
                    visual.Bind(actor);visual.DrawPose(actor.poseTime);
                    Require(visual.DrawnClip==expectedPose && visual.DrawnFrame==0 &&
                        (Vector2)visual.transform.position==actor.BodyPosition && visual.transform.localScale.x==facing,
                        $"Представление не повторяет вычисленную стойку: head={head}, clip={visual.DrawnClip}, frame={visual.DrawnFrame}.");
                    var parts=PonyRenderSelection.Frames(PonyAppearance.Snapshot(actor),expectedPose)[0];
                    Require(parts.Count(part=>part.sourceShape==PonyHeadRenderResolver.SourceShape)==1 &&
                        sample.PartIndex!=parts.Single(part=>part.sourceShape==PonyHeadRenderResolver.SourceShape).sourceIndex,
                        $"Декоративная голова стала опорной частью: head={head}, footPart={sample.PartIndex}.");
                    var actual=visual.GetComponentsInChildren<MeshFilter>().Where(filter=>filter.GetComponent<MeshRenderer>().enabled).ToArray();
                    Require(actual.Select(filter=>filter.sharedMesh).SequenceEqual(parts.Select(part=>part.mesh)) &&
                        actual.Select(filter=>filter.GetComponent<MeshRenderer>().sharedMaterial).SequenceEqual(parts.Select(part=>part.material)),
                        $"Меши/материалы наклонной стойки расходятся с выбранной головой: head={head}, pose={expectedPose}.");
                    Require(visual.TryGetDrawnBounds(out var bounds) && bounds.min.x>=Viewport.xMin && bounds.max.x<=Viewport.xMax &&
                        bounds.min.y>=Viewport.yMin && bounds.max.y<=Viewport.yMax,$"Стойка обрезана камерой каталога: head={head}, bounds={bounds}.");
                    evidence.observations.Add(new Observation {headStyleId=head,pose=expectedPose,slopeDirection=direction,
                        facing=facing,frame=0,footPartIndex=sample.PartIndex,anchor=actor.Position,body=sample.BodyPosition,contact=sample.ContactPoint});
                    evidence.computedStances++;
                }
                var camera=new GameObject("Формы головы на физических наклонах").AddComponent<Camera>();objects.Add(camera.gameObject);
                camera.enabled=false;camera.orthographic=true;camera.rect=new Rect(0,0,1,1);camera.aspect=2;
                camera.orthographicSize=Viewport.height*.5f;
                camera.transform.position=new Vector3(Viewport.center.x,Viewport.center.y,-10);
                camera.backgroundColor=new Color(.12f,.14f,.16f);
                camera.projectionMatrix=Matrix4x4.Ortho(-Viewport.width*.5f,Viewport.width*.5f,-Viewport.height*.5f,Viewport.height*.5f,
                    camera.nearClipPlane,camera.farClipPlane);
                string name=$"ten-heads-native-slope-{(direction>0?"rise":"fall")}-facing-{(facing>0?"right":"left")}.png";
                Runtime.NetworkFrameCapture.Save(camera,Path.Combine(directory,name),width:2880,height:1440);
                evidence.nativeCaptures++;
            }
            finally
            {
                foreach(var actor in boundActors) ActorBodySupport.Unbind(actor);
                foreach(var obj in objects) if(obj!=null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private static RoomLayout Fixture(int direction)
        {
            int count=PonyHeadCatalog.Styles.Count;var surfaces=new RoomSurface[count];var artwork=new RoomArtwork[count];
            Require(count==10,$"Для сетки сравнения ожидается 10 форм головы, получено {count}.");
            for(int i=0;i<count;i++)
            {
                var surface=new RoomSurface {x=i%5*6,y=-(i/5)*6,width=4,height=2,slopeDirection=direction,
                    oneWay=true,shotsPassThrough=true,movementSound="metal",texture="Hub/floor"};
                surfaces[i]=surface;
                artwork[i]=new RoomArtwork {x=surface.x,y=surface.y,width=surface.width,height=surface.height,
                    slopeDirection=surface.slopeDirection,texture=surface.texture,repeat=false};
            }
            return new RoomLayout {id="head-slope-check",left=Viewport.xMin,right=Viewport.xMax,bottom=Viewport.yMin,top=8,
                platforms=surfaces,artwork=artwork,ladders=Array.Empty<RoomLadder>()};
        }
        private static void Require(bool valid,string detail)
        {if(!valid) throw new InvalidOperationException("Проверка форм головы на наклонах: "+detail);}
    }
}
