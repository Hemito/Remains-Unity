using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class StandingEdgePoseChecks
    {
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Стоячие краевые позы: "+detail);}
        public static void Run()
        {
            Anatomy.Load();StandingEdgePoseCatalog.Load();PonyVisual.Prepare();
            var manifest=StateJson.FromJson<StandingEdgePoseManifest>(GameAssets.Load<TextAsset>("Content/standing-edge-poses").text);
            CheckSelection(manifest);
            var audio=StateJson.FromJson<MovementAudio.Settings>(GameAssets.Load<TextAsset>("Presentation/movement-audio").text);
            foreach(var profile in manifest.profiles)
            foreach(var pose in profile.candidates)
                Require(audio.quietPoses.Count(id=>id==pose.poseId)==1 && audio.rhythms.All(rhythm=>rhythm.pose!=pose.poseId),
                    "Краевая неподвижная поза не объявлена явно тихой: "+pose.poseId);
            foreach(var definition in manifest.profiles)
            foreach(var pose in definition.candidates.Concat(new[]{definition.standing,definition.crouching}))
            {
                var actor=new ActorState {anatomy=definition.anatomy,pose=pose.poseId};
                Require(Anatomy.TrySupportContacts(actor,out var actual) && actual.Count==pose.contacts.Length,"Контакты графики отсутствуют: "+pose.poseId);
                for(int i=0;i<actual.Count;i++)
                    Require(Mathf.Abs(actual[i].x-pose.contacts[i].lowestX)<.000001f &&
                        Mathf.Abs(actual[i].y-pose.contacts[i].minimumY)<.000001f,"Контакты двух потребителей разошлись: "+pose.poseId+"/"+i);
                var clip=Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose.poseId);
                Require(clip.frames.Length==1,"Краевая поза зависит от времени: "+pose.poseId);
                foreach(float time in new[]{0f,.2f,10f}) Require(PonyMotionFrames.Sample(actor,time)==0,"Одно-кадровая поза дрейфует: "+pose.poseId);
            }
            var visual=new GameObject("Проверка стоячих краевых поз").AddComponent<PonyVisual>();
            try
            {
                foreach(var definition in manifest.profiles)
                {
                    var actor=new ActorState {id="standing-edge-drawn",anatomy=definition.anatomy};visual.Bind(actor);
                    foreach(var pose in definition.candidates)
                    {
                        actor.pose=pose.poseId;ActorPose.Advance(actor,1);visual.DrawPose(actor.poseTime);
                        Require(visual.DrawnClip==pose.poseId && visual.DrawnFrame==0,"Рисунок не выбрал declared одно-кадровую позу: "+pose.poseId);
                    }
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
            Debug.Log("Стоячие краевые позы: PASS — ресурсные 44 позы, actual feet, край/зеркало/приседание, стыки и настоящие пробелы, монотонный rank, явный MissingContact и наклон; collision hull не изменён.");
        }
        // File-data harness may invoke this pure part; Run additionally verifies
        // native Resources, the shared anatomy sampler and motion frame consumer.
        public static void CheckSelection(StandingEdgePoseManifest manifest)
        {
            var definition=Array.Find(manifest.profiles,p=>p.anatomy=="pony");
            Require(definition!=null && definition.candidates.Length==44,"Нет полного authored набора 44 поз");
            var profile=new StandingEdgePoseProfile(definition);
            foreach(var pose in definition.candidates)
            {
                Require(pose.sourceSprite==3809 && pose.contacts.Length==4 && pose.contacts.Count(p=>p.eligible)==2,"Ресурсная привязка/опорные роли: "+pose.poseId);
                Require(pose.sourceFrame>=27 && pose.sourceFrame<=70,"Неизвестный исходный кадр: "+pose.poseId);
            }
            var left=new[]{new StandingSupportInterval(-10,0)};
            var right=new[]{new StandingSupportInterval(0,10)};
            var full=new[]{new StandingSupportInterval(-10,10)};
            StandingEdgePoseResult Select(float x,int facing=1,bool crouched=false,StandingSupportInterval[] supports=null)
                =>StandingEdgePoseSelector.Select(profile,crouched,facing,x,supports??left);
            Require(Select(0,supports:full).Outcome==StandingEdgePoseOutcome.Standard,"Обычный пол потерял idle");
            Require(Select(0,crouched:true,supports:full).PoseId=="crouch","Обычный пол потерял crouch");
            Require(definition.standing.contacts.All(p=>.45f+p.visibleMinimumX>0),"Исходный counterexample не воспроизведён полными видимыми ногами");
            var edge=Select(.45f);Require(edge.Outcome==StandingEdgePoseOutcome.Selected && edge.PoseId=="edgeStandBack04" &&
                edge.ProjectedFeetContacting==1 && edge.ProjectedFeetCovered==0,"Нет частично опёртой подошвы при x=.45: "+edge.PoseId);
            Require(Select(-.45f,-1,supports:right).PoseId==edge.PoseId,"Зеркало изменило поддержку rear bank");
            Require(Select(.45f,-1).PoseId.StartsWith("edgeStandFront"),"Facing не выбрал противоположную authored bank");
            Require(Select(-.45f,supports:right).PoseId.StartsWith("edgeStandFront"),"Противоположный край не выбрал front bank");
            Require(Select(.45f,crouched:true).PoseId.StartsWith("edgeCrouchBack"),"Приседание использует standing bank");
            Require(Select(-.45f,crouched:true,supports:right).PoseId.StartsWith("edgeCrouchFront"),"Приседание у противоположного края потеряло front bank");
            Require(Select(.45f,supports:Array.Empty<StandingSupportInterval>()).Outcome==StandingEdgePoseOutcome.MissingContact,"Пустая опора выдала гарантированную позу");
            var gap=new[]{new StandingSupportInterval(-10,-2),new StandingSupportInterval(2,10)};
            Require(Select(0,supports:gap).Outcome==StandingEdgePoseOutcome.MissingContact,"Промежуток между опорами заполнен фиктивным полом");
            var seams=new[]{new StandingSupportInterval(.1f,10),new StandingSupportInterval(-.1f,.1f),new StandingSupportInterval(-10,-.1f)};
            Require(Select(0,supports:seams).Outcome==StandingEdgePoseOutcome.Standard,"Смежные same-plane интервалы не объединены");
            Require(StandingEdgePoseSelector.Select(profile,false,1,0,full,true).Outcome==StandingEdgePoseOutcome.NotApplicable,"Наклон молча обработан как плоский пол");
            Require(StandingEdgePoseSelector.Select(null,false,1,0,full).Outcome==StandingEdgePoseOutcome.NotApplicable,"Необъявленная анатомия получила pony bank");
            var pad=new StandingFootPadDefinition {shape="custom",path="foot",eligible=true,minimumX=-.3f,maximumX=-.1f,
                visibleMinimumX=-.3f,visibleMaximumX=-.1f,minimumY=0,lowestX=-.2f,visibleMaximumY=.1f};
            StandingFootPoseDefinition Pose(string id,string side,bool crouched=false)=>new() {poseId=id,side=side,crouched=crouched,contacts=new[]{pad}};
            var splitProfile=new StandingEdgePoseProfile(new StandingEdgePoseProfileDefinition {anatomy="split-check",standing=Pose("normal","normal"),
                crouching=Pose("sit","normal",true),candidates=new[]{Pose("edge","back")}});
            var split=new[]{new StandingSupportInterval(-.4f,-.31f),new StandingSupportInterval(-.09f,0)};
            Require(StandingEdgePoseSelector.Select(splitProfile,false,1,0,split).Outcome==StandingEdgePoseOutcome.MissingContact,
                "Подошва перекрыла настоящий пробел между близкими опорами");
            foreach(bool crouched in new[]{false,true})
            {
                int previous=-1;
                for(int i=0;i<=49;i++)
                {
                    var result=Select(i*.01f,crouched:crouched);
                    Require(result.Outcome!=StandingEdgePoseOutcome.MissingContact,"Поза не покрыла legacy hull-support диапазон: "+i+"/"+crouched);
                    if(result.Outcome==StandingEdgePoseOutcome.Standard) continue;
                    var selected=Array.Find(definition.candidates,p=>p.poseId==result.PoseId);
                    Require(selected.rank>=previous,"Rank пошёл назад при увеличении свешивания: "+i+"/"+result.PoseId);previous=selected.rank;
                    Require(result.ProjectedFeetContacting>0,"Выбрана поза без реального пересечения подошвы");
                }
            }
            // Prepared profiles cannot be changed by a caller modifying JSON data.
            float savedMinimum=definition.candidates[4].contacts[3].minimumX;
            definition.candidates[4].contacts[3].minimumX=100;
            Require(Select(.45f).PoseId==edge.PoseId,"Профиль разделяет изменяемые данные manifest");
            definition.candidates[4].contacts[3].minimumX=savedMinimum;
        }
    }
}
