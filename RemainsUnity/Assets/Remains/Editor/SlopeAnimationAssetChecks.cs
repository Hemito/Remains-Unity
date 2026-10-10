using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Resource/sampler checks. These do not prove terrain foot-fit or original-runtime equivalence.
    public static class SlopeAnimationAssetChecks
    {
        private static readonly string[] Ids={"slopeStandUp","slopeStandDown","slopeWalkUp","slopeWalkDown"};
        public static void Run()
        {
            Anatomy.Load();PonyVisual.Prepare();
            var library=PonyAnimationData.Library;
            var compiled=Resources.Load<PonyRenderAsset>("Original/Pony/render");
            Require(compiled!=null,"Нет запечённого каталога пони");compiled.Validate();
            var catalog=Remains.Serialization.StateJson.FromJson<AnatomyCatalog>(Resources.Load<TextAsset>("Content/anatomy").text);
            var contacts=Array.Find(catalog.anatomies,a=>a.id=="pony").supportContacts;
            var pixelCache=new Dictionary<string,(int width,int height,Color32[] pixels)>();
            int frames=0,selections=0,drawn=0;
            foreach(string id in Ids)
            {
                var clip=Array.Find(library.clips,c=>c.id==id);
                bool standing=id.StartsWith("slopeStand",StringComparison.Ordinal);
                Require(clip!=null && clip.fps==30 && clip.frames.Length==(standing?1:17) &&
                    clip.nonLooping==standing && clip.playIntro==!standing && clip.loopStartFrame==(standing?0:1),
                    "Неверные явные вход/цикл наклонной графики: "+id);
                var rendered=Array.Find(compiled.Clips,c=>c.id==id);
                var track=Array.Find(contacts.poses,p=>p.pose==id);
                Require(rendered?.frames.Length==clip.frames.Length && track?.frames.Length==clip.frames.Length,
                    "Неполная графика или контакты наклонной позы: "+id);
                for(int frame=0;frame<clip.frames.Length;frame++)
                {
                    frames++;var parts=clip.frames[frame].parts;var cached=rendered.frames[frame].parts;
                    Require(cached.Length==parts.Length,$"Потеряна часть: pose={id}, frame={frame}");
                    int foot=0;
                    for(int part=0;part<parts.Length;part++)
                    {
                        var source=parts[part];var mesh=cached[part].mesh;
                        Require(mesh.name==$"mesh:{id}:{frame}:{part}" && cached[part].material.mainTexture.name==source.texture,
                            $"Изменена ресурсная привязка: pose={id}, frame={frame}, part={part}");
                        var quad=PonyAnimationData.Quad(source);var vertices=mesh.vertices;
                        Require(vertices.Length==4,"Неверный quad: "+mesh.name);
                        for(int corner=0;corner<4;corner++) Require((vertices[corner]-quad[corner]).sqrMagnitude<1e-12f,
                            $"Запечённая матрица не совпала с источником: pose={id}, frame={frame}, part={part}, corner={corner}");
                        if(Array.IndexOf(contacts.shapes,source.shape)<0) continue;
                        var lowest=LowestPixel(source,contacts.alphaMinimum,pixelCache);
                        Require(foot<track.frames[frame].points.Length &&
                            (lowest-track.frames[frame].points[foot]).sqrMagnitude<1e-10f,
                            $"Контакт не совпал с реальным PNG: pose={id}, frame={frame}, shape={source.shape}, path={source.path}");
                        foot++;
                    }
                    Require(foot==4 && track.frames[frame].points.Length==4,$"Нет четырёх видимых опор: pose={id}, frame={frame}");
                }
                for(int hair=1;hair<=library.hairStyles;hair++)
                for(int eye=1;eye<=library.eyeStyles;eye++)
                for(int secondary=0;secondary<2;secondary++)
                {
                    var key=new PonyAppearanceKey(hair,eye,secondary==1,0xFFFFFF,0,0xFFFFFF,0,0);
                    var selected=PonyRenderSelection.Frames(key,id);
                    Require(selected.Length==clip.frames.Length,"Внешность потеряла наклонный кадр: "+id);
                    foreach(var frame in selected)
                    {
                        selections++;Require(frame.Length>0,"Пустая внешность: "+id);
                        for(int part=1;part<frame.Length;part++) Require(frame[part-1].drawOrder<frame[part].drawOrder,
                            "Нарушен порядок частей наклонной внешности: "+id);
                    }
                }
                foreach(int facing in new[]{-1,1})
                {
                    var actor=new ActorState {id="slope-art-"+id+facing,player=true,connected=true,pose=id,facing=facing};
                    var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
                    try
                    {
                        visual.Bind(actor);var camera=PonyVisual.CameraBounds(actor);
                        for(int frame=0;frame<clip.frames.Length;frame++)
                        {
                            actor.poseTime=(frame+.25f)/30;
                            CheckDraw(actor,visual,frame,track);drawn++;
                            Require(PonyVisual.CameraBounds(actor)==camera,"Камерная рамка меняется от наклонного кадра");
                            Require(visual.TryGetDrawnBounds(out var bounds) && Contains(camera,bounds),
                                $"Кадр вне полной камерной рамки: pose={id}, frame={frame}, facing={facing}");
                        }
                        foreach(int rate in new[]{30,60,144})
                        {
                            actor.poseTime=0;
                            for(int tick=0;tick<=rate;tick++)
                            {
                                // Independent expected source span: entry1 once, then source2..17.
                                int step=(int)Math.Floor((double)actor.poseTime*30+.0001);
                                int expected=standing?0:step==0?0:1+(step-1)%16;
                                CheckDraw(actor,visual,expected,track);drawn++;
                                ActorPose.Advance(actor,1f/rate);
                            }
                        }
                    }
                    finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
                }
            }
            foreach(string side in new[]{"Up","Down"})
            {
                var stand=Array.Find(library.clips,c=>c.id=="slopeStand"+side);
                var walk=Array.Find(library.clips,c=>c.id=="slopeWalk"+side);
                Require(JsonUtility.ToJson(stand.frames[0])==JsonUtility.ToJson(walk.frames[0]),
                    "Остановка отличается от исходного входного кадра: "+side);
                Require(JsonUtility.ToJson(walk.frames[0])!=JsonUtility.ToJson(walk.frames[1]),
                    "Вход ошибочно заменён первым кадром цикла: "+side);
            }
            Require(frames==36 && selections==2160,"Не проверен полный новый графический набор");
            Debug.Log($"Наклонная графика: PASS — 4 позы/36 кадров, {selections} внешностей, {drawn} actual DrawPose, PNG-контакты всех ног, обе стороны, общий sampler30/60/144 и стабильная камерная рамка. Прилегание к terrain и оригинальный runtime этой проверкой не доказаны.");
        }
        private static void CheckDraw(ActorState actor,PonyVisual visual,int expected,PoseSupportTrack track)
        {
            Require(PonyMotionFrames.Sample(actor,actor.poseTime)==expected,"Общий sampler выбрал другой наклонный кадр: "+actor.id);
            visual.DrawPose(actor.poseTime);
            Require(visual.DrawnClip==actor.pose && visual.DrawnFrame==expected,"DrawPose расходится с общим sampler: "+actor.id);
            Require(Anatomy.TrySupportContacts(actor,out var points) && points.Count==4,"Нет контактов выбранного кадра: "+actor.id);
            for(int i=0;i<points.Count;i++) Require(points[i]==track.frames[expected].points[i],"Anatomy выбрала другую фазу контакта: "+actor.id);
            Require(Anatomy.TryRegion(actor,"legs",out _,out _),"Наклонный кадр потерял область legs: "+actor.id);
        }
        private static Vector2 LowestPixel(PonyPart part,int alpha,Dictionary<string,(int width,int height,Color32[] pixels)> cache)
        {
            if(!cache.TryGetValue(part.texture,out var data))
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    string path=PonyContactSource.Png(part.texture);
                    Require(ImageConversion.LoadImage(texture,File.ReadAllBytes(path)),"Не декодирован PNG опоры: "+path);
                    data=(texture.width,texture.height,texture.GetPixels32());cache.Add(part.texture,data);
                }
                finally {UnityEngine.Object.DestroyImmediate(texture);}
            }
            var lowest=new Vector2(0,float.PositiveInfinity);var b=part.bounds;
            // Same declared alpha/centre convention; Unity independently decodes the production PNG.
            for(int y=data.height-1;y>=0;y--)
            for(int x=0;x<data.width;x++)
            {
                if(data.pixels[y*data.width+x].a<alpha) continue;
                var source=new Vector2(Mathf.Lerp(b[0],b[2],(x+.5f)/data.width),Mathf.Lerp(b[3],b[1],(y+.5f)/data.height));
                var point=PonyAnimationData.TransformPoint(part,source);
                if(point.y<lowest.y) lowest=point;
            }
            Require(float.IsFinite(lowest.y),"Нет непрозрачных пикселей опоры: "+part.texture);return lowest;
        }
        private static bool Contains(Bounds outer,Bounds inner)=>inner.min.x>=outer.min.x-.0001f && inner.max.x<=outer.max.x+.0001f &&
            inner.min.y>=outer.min.y-.0001f && inner.max.y<=outer.max.y+.0001f;
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException(reason);}
    }
}
