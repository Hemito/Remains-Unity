using System;
using System.Collections;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Content;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class NetworkAppearanceProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private bool finished;
        private string forcedHeadId;
        public void Initialize(PrototypeRuntime value)
        {
            PonyHeadCatalog.Load();
            var arguments=Environment.GetCommandLineArgs();
            for(int i=0;i<arguments.Length;i++)
            {
                if(arguments[i]!="--appearance-head-id") continue;
                if(forcedHeadId!=null) throw new InvalidOperationException("Параметр проверки --appearance-head-id указан несколько раз.");
                if(i+1>=arguments.Length || arguments[i+1].StartsWith("--",StringComparison.Ordinal))
                    throw new InvalidOperationException("После --appearance-head-id нужен стабильный ID формы головы.");
                forcedHeadId=PonyHeadCatalog.Get(arguments[++i]).id;
            }
            runtime=value;deadline=Time.unscaledTime+30;
            if(value.Options.Character.StartsWith("session-client-",StringComparison.Ordinal))
            {
                if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверке палитры нужен отдельный профиль.");
                int index=int.Parse(value.Options.Character.Substring("session-client-".Length));
                value.Session.Saves.SaveCharacter(new ActorState {id="player:"+value.Options.Character,character=value.Options.Character,player=true,hair=5+index,appearance=Expected(index)});
            }
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,"Таймаут передачи внешности.");return;}
            var session=runtime.Session;var actors=session.World?.actors.Where(a=>a.player && a.connected).ToArray();
            if(session.LocalActor==null || actors==null || actors.Length!=runtime.Options.ProbePeers) return;
            if(actors.Single(a=>a.character=="session-client-1").hair!=6 || actors.Select(a=>a.hair).Distinct().Count()!=actors.Length)
            {Finish(false,"Импортированный цвет заменён либо новые персонажи получили одинаковые цвета.");return;}
            foreach(var actor in actors)
            {
                if(actor.character=="session-host")
                {if(actor.appearance!=null) {Finish(false,"Старый профиль хоста изменён новой внешностью.");return;}continue;}
                int index=int.Parse(actor.character.Substring("session-client-".Length));
                var expected=new ActorState {appearance=Expected(index)};
                if(!PonyAppearance.Snapshot(actor).Equals(PonyAppearance.Snapshot(expected)))
                {Finish(false,"Внешность не совпала после передачи: "+actor.character);return;}
            }
            if(runtime.Options.Smoke.Contains("-frames-"))
            {
                // Join publishes actors before the room's lighting/visibility gate.
                // Capture the same ready presentation that a player actually sees.
                if(!runtime.LevelPresentationReady || runtime.IsLevelPreparing) return;
                var visuals=FindObjectsByType<PonyVisual>(FindObjectsSortMode.None);
                if(actors.Any(actor=>!visuals.Any(visual=>visual.ActorId==actor.id))) return;
                foreach(var actor in actors)
                {
                    var visual=visuals.Single(v=>v.ActorId==actor.id);visual.DrawPose(actor.poseTime);
                    var key=PonyAppearance.Snapshot(actor);var frame=PonyRenderSelection.Frames(key,visual.DrawnClip)[visual.DrawnFrame];
                    var meshes=visual.GetComponentsInChildren<MeshFilter>().Where(m=>m.GetComponent<MeshRenderer>().enabled).ToArray();
                    if(!meshes.Select(m=>m.sharedMesh).SequenceEqual(frame.Select(p=>p.mesh)))
                    {Finish(false,"Сетевая внешность не применена к графике: "+actor.character);return;}
                    var block=new MaterialPropertyBlock();
                    for(int i=0;i<meshes.Length;i++)
                    {
                        meshes[i].GetComponent<MeshRenderer>().GetPropertyBlock(block);
                        if(block.GetColor("_Color")!=PonyAppearance.Tint(frame[i].slot,key))
                        {Finish(false,"Сетевой цвет не применён: "+actor.character+"/"+frame[i].slot);return;}
                    }
                }
                NetworkFrameCapture.Save(runtime.WorldCamera,Path.ChangeExtension(runtime.Options.Report,"png"));
            }
            session.Saves.SaveCharacter(session.LocalActor);
            var loaded=session.Saves.LoadCharacter();
            bool saved=loaded.hair==session.LocalActor.hair && PonyAppearance.Snapshot(loaded).Equals(PonyAppearance.Snapshot(session.LocalActor));
            Finish(saved,saved?"Все участники получили отдельные формы головы/гривы/глаз, пять цветов и второй цвет; старый хост сохранён, локальный профиль совпадает. При frames проверены реальные меши и цвета каждого персонажа.":"Локальное сохранение потеряло внешность.");
        }
        private CharacterAppearance Expected(int index)=>new()
        {version=CharacterAppearance.CurrentVersion,headStyleId=forcedHeadId??PonyHeadCatalog.Styles[index%PonyHeadCatalog.Styles.Count].id,
            hairStyle=6-index,eyeStyle=7-index,fur=0xA3A3A3+index*0x010101,hair=0x225588+index*0x080005,secondaryHair=0xEE2288-index*0x001100,
            eyes=0x22AA44+index*0x000022,magic=0xFFAA00-index*0x110000,secondaryHairEnabled=index%2==1};
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,forcedHeadId=forcedHeadId},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(3);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;public string forcedHeadId;}
    }
}
