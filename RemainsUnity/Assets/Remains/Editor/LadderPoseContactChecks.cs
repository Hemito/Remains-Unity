using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class LadderPoseContactChecks
    {
        public static void Run()
        {
            Anatomy.Load();PonyVisual.Prepare();
            CheckVisibleFeet();
            var settings=MovementSettings.Load();
            var room=new RoomLayout {id="ladder-pose-contact",left=-5,right=5,bottom=-1,top=6,
                ladders=new[]{new RoomLadder {x=0,bottom=0,top=3}},
                platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1}}};
            var counterexample=new ActorState {id="climb-visible-floor",y=.55f,vy=-settings.climbSpeed*settings.climbDownMultiplier,
                climbing=true,pose="climb",poseTime=.1f};
            Require(PonyMotionFrames.Sample(counterexample,counterexample.poseTime)==4,"Контрпример должен использовать настоящий кадрclimb4");
            var nextBody=counterexample.Position+Vector2.up*(counterexample.vy/60);
            Require(nextBody.y+GraphicLowest("climb",4,"61/1/1","25").y>0,
                "Контрпример должен проходить прежнюю неполную проверку верхней части ноги");
            Require(nextBody.y+GraphicLowest("climb",4,"57/1/1","298").y<0,
                "Настоящее пропущенное копыто должно пересечь пол при том же следующем положении");
            Require(LadderPoseContact.ShouldRelease(counterexample,room,nextBody),
                "Нижняя видимая нога не обнаружила твёрдый пол раньше габарита тела");
            new MovementController(room).Step(counterexample,Vector2.down,false,1f/60);
            Require(!counterexample.climbing && counterexample.y>0 && counterexample.vy<0,"Контакт ноги не отпустил хват с сохранением спуска до приземления тела");
            var idle=new ActorState {id="unchanged-idle",pose="idle"};
            Require(Anatomy.TrySupportContacts(idle,out var idleContacts) && MinimumY(idleContacts)<0,
                "Исходное положение копытidle неожиданно исправлено вместе с данными контакта");
            room.platforms[0].oneWay=true;
            var beam=new ActorState {id="climb-one-way",y=.1f,vy=-4.725f,climbing=true,pose="climb",poseTime=.1f};
            Require(!LadderPoseContact.ShouldRelease(beam,room,beam.Position+Vector2.down*.08f),"Хват преждевременно отпущен у проходимой балки");
            room.platforms[0].oneWay=false;
            var trace=new StringBuilder("rate,facing,startPhase,tick,y,vy,poseTime,sampledClimbFrame,drawnClip,drawnFrame,heldFootMinimumY,climbing\n");
            foreach(int rate in new[]{30,60,144})
            foreach(int facing in new[]{-1,1})
            foreach(float phase in new[]{0f,.1f,.2f,.3f})
            {
                float dt=1f/rate;
                room.ladders[0].facing=(LadderFacing)facing;
                var actor=new ActorState {id=$"ladder-contact-{rate}-{facing}-{phase:R}",x=0,y=2.25f,climbing=true,
                    facing=facing,pose="climb",poseTime=phase,vy=-settings.climbSpeed*settings.climbDownMultiplier};
                var movement=new MovementController(room,settings);
                var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
                try
                {
                    visual.Bind(actor);
                    bool released=false;int heldSamples=0;
                    for(int tick=0;tick<rate*2;tick++)
                    {
                        ActorPose.Advance(actor,dt);
                        int heldFrame=actor.climbing?PonyMotionFrames.Sample(actor,actor.poseTime):-1;
                        float localMinimum=0;
                        if(actor.climbing)
                        {
                            Require(Anatomy.TrySupportContacts(actor,out var contacts),"Нет контактов выбранного кадра");
                            localMinimum=MinimumY(contacts);
                        }
                        bool heldBefore=actor.climbing;
                        movement.Step(actor,Vector2.down,false,dt);
                        visual.DrawPose(actor.poseTime);
                        if(actor.climbing)
                        {
                            heldSamples++;
                            Require(visual.DrawnClip=="climb" && visual.DrawnFrame==heldFrame,"Контакты и PonyVisual выбрали разные кадры хвата");
                            Require(actor.y+localMinimum>=-.0001f,$"Видимая нога прошла пол до отпускания: rate={rate}, phase={phase:R}, frame={heldFrame}, y={actor.y:R}, footY={actor.y+localMinimum:R}");
                        }
                        if(heldBefore && !actor.climbing) released=true;
                        Require(actor.y>=-.0001f,"Тело прошло твёрдый нижний пол");
                        trace.AppendLine(string.Join(",",rate,facing,F(phase),tick,F(actor.y),F(actor.vy),F(actor.poseTime),heldFrame,
                            visual.DrawnClip,visual.DrawnFrame,F(heldFrame<0?float.NaN:actor.y+localMinimum),actor.climbing));
                    }
                    Require(released && heldSamples>0 && !actor.climbing && Mathf.Abs(actor.y)<.0001f && actor.vy==0,
                        $"Спуск не завершился устойчивой опорой: rate={rate}, facing={facing}, phase={phase:R}, position={actor.Position}");
                }
                finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
            }
            var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/ladder-pose-contact"));
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"descent.csv"),trace.ToString());
            Debug.Log("Контакт ног при спуске: PASS — четыре нижние ноги: прежние13поз/267кадров и44краевые позы/44кадра, отдельная от урона декларация, реальные PNG копыт3/102/298 и PonyVisual кадры, твёрдый пол, прежняя скорость/гравитация, проход балки, оба направления и30/60/144Гц. Trace: "+directory);
        }
        private static void CheckVisibleFeet()
        {
            var catalog=Remains.Serialization.StateJson.FromJson<AnatomyCatalog>(Resources.Load<TextAsset>("Content/anatomy").text);
            var pony=Array.Find(catalog.anatomies,value=>value.id=="pony");
            var shapes=pony.supportContacts.shapes;
            Require(shapes.Length==4 && Array.TrueForAll(new[]{"3","102","275","298"},shape=>Array.IndexOf(shapes,shape)>=0),
                "Декларация опор потеряла прямое или согнутое переднее/заднее копыто");
            var damageLegs=Array.Find(pony.regions,value=>value.id=="legs");
            Require(damageLegs.shapes.Length==2 && damageLegs.shapes[0]=="25" && damageLegs.shapes[1]=="275",
                "Исправление контактов изменило независимые области урона");
            int count=0,edgeCount=0,legacyPoses=0;
            foreach(var clip in PonyAnimationData.Library.clips)
            {
                // The new slope contact set has its own asset/controller verification.
                if(clip.id is "slopeStandUp" or "slopeStandDown" or "slopeWalkUp" or "slopeWalkDown") continue;
                bool edge=clip.id.StartsWith("edgeStand",StringComparison.Ordinal) || clip.id.StartsWith("edgeCrouch",StringComparison.Ordinal);
                if(edge) Require(clip.frames.Length==1,"Краевая поза перестала быть одно-кадровой: "+clip.id);
                else legacyPoses++;
                for(int frame=0;frame<clip.frames.Length;frame++)
                {
                    Require(Array.FindAll(clip.frames[frame].parts,part=>Array.IndexOf(shapes,part.shape)>=0).Length==4,
                        $"Не объявлены все четыре нижние ноги: pose={clip.id}, frame={frame}");
                    count++;
                    if(edge) edgeCount++;
                }
            }
            Require(legacyPoses==13 && count-edgeCount==267 && edgeCount==44 && count==311,"Проверяемый ресурс изменился: пересмотрите полноту декларации копыт");
            foreach(var expected in new Dictionary<string,int> {{"idle",1},{"walk",17},{"slowWalk",25},{"run",58},{"jump",32},{"climb",13},
                {"down",40},{"crouch",1},{"crawl",14},{"crouchLower",9},{"crouchRise",9},{"landing",6},{"fall",42}})
                Require(Array.Find(PonyAnimationData.Library.clips,clip=>clip.id==expected.Key)?.frames.Length==expected.Value,
                    "Прежняя поза потеряла свои кадры: "+expected.Key);
            CheckDrawnFoot("idle",0,0,"57/1/1","3",-.036454f);
            CheckDrawnFoot("climb",4,.1f,"57/1/1","298",-.509750f);
            CheckDrawnFoot("crouch",0,0,"frleg3/1/1","102",-.027330f);
            CheckDrawnFoot("crawl",9,.3f,"frleg3/1/1","102",-.065410f);
        }
        private static void CheckDrawnFoot(string pose,int frame,float phase,string path,string shape,float expectedY)
        {
            var actor=new ActorState {id="graphic-contact-"+pose,pose=pose,poseTime=phase,
                climbing=pose=="climb",vy=pose=="climb"?-1:0};
            Require(PonyMotionFrames.Sample(actor,actor.poseTime)==frame,$"Проверка выбрала другой кадр: pose={pose}");
            var pixel=GraphicLowest(pose,frame,path,shape);
            Require(Mathf.Abs(pixel.y-expectedY)<.00001f,$"Исходное копыто изменилось: pose={pose}, path={path}, y={pixel.y:R}");
            Require(Anatomy.TrySupportContacts(actor,out var contacts) && Contains(contacts,pixel),
                $"Настоящее видимое копыто пропущено: pose={pose}, frame={frame}, path={path}, shape={shape}, point={pixel}");
            var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
            try
            {
                visual.Bind(actor);visual.DrawPose(actor.poseTime);
                Require(visual.DrawnClip==pose && visual.DrawnFrame==frame,$"Контакт и рисунок выбрали разные кадры: pose={pose}");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }
        // Test-only PNG measurement. The runtime still reads only the baked contacts.
        private static Vector2 GraphicLowest(string pose,int frame,string path,string shape)
        {
            var clip=Array.Find(PonyAnimationData.Library.clips,value=>value.id==pose);
            var part=Array.Find(clip.frames[frame].parts,value=>value.path==path && value.shape==shape);
            Require(part!=null,$"Нет проверяемого glyph: pose={pose}, frame={frame}, path={path}, shape={shape}");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                string file=Path.Combine(Application.dataPath,"Remains/Resources/Original/Pony",part.texture+".png");
                Require(ImageConversion.LoadImage(texture,File.ReadAllBytes(file)),"PNG копыта не декодирован: "+file);
                var pixels=texture.GetPixels32();var bounds=part.bounds;
                var lowest=new Vector2(0,float.PositiveInfinity);
                for(int y=0;y<texture.height;y++)
                for(int x=0;x<texture.width;x++)
                {
                    if(pixels[y*texture.width+x].a<192) continue;
                    var source=new Vector2(Mathf.Lerp(bounds[0],bounds[2],(x+.5f)/texture.width),
                        Mathf.Lerp(bounds[3],bounds[1],(y+.5f)/texture.height));
                    var point=PonyAnimationData.TransformPoint(part,source);
                    if(point.y<lowest.y) lowest=point;
                }
                Require(float.IsFinite(lowest.y),"Нет непрозрачного контакта: "+file);
                return lowest;
            }
            finally {UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static bool Contains(IReadOnlyList<Vector2> contacts,Vector2 expected)
        {
            foreach(var point in contacts) if((point-expected).sqrMagnitude<.00000001f) return true;
            return false;
        }
        private static float MinimumY(IReadOnlyList<Vector2> contacts)
        {
            float result=float.PositiveInfinity;foreach(var point in contacts) result=Mathf.Min(result,point.y);return result;
        }
        private static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        private static void Require(bool valid,string context)
        {
            if(!valid) throw new InvalidOperationException("Контакт ног лестницы: "+context);
        }
    }
}
