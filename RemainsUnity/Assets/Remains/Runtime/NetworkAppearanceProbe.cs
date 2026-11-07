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
        private const double AdmissionTimeoutSeconds=60,PresentationTimeoutSeconds=30;
        private PrototypeRuntime runtime;
        private double startedAt;
        private double? admittedAt;
        private int connectedPeers;
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
            runtime=value;
            if(value.Options.Character.StartsWith("session-client-",StringComparison.Ordinal))
            {
                if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверке палитры нужен отдельный профиль.");
                int index=int.Parse(value.Options.Character.Substring("session-client-".Length));
                value.Session.Saves.SaveCharacter(new ActorState {id="player:"+value.Options.Character,character=value.Options.Character,player=true,hair=5+index,appearance=Expected(index)});
            }
            startedAt=Time.realtimeSinceStartupAsDouble;
        }
        private void Update()
        {
            if(finished) return;
            double now=Time.realtimeSinceStartupAsDouble;
            var session=runtime.Session;var actors=session.World?.actors.Where(a=>a.player && a.connected).ToArray();
            connectedPeers=actors?.Length??0;
            if(session.LocalActor==null || connectedPeers!=runtime.Options.ProbePeers)
            {
                if(now-startedAt>=AdmissionTimeoutSeconds)
                    Finish(false,$"Истекло ожидание участников: {connectedPeers}/{runtime.Options.ProbePeers}, подготовка входа={session.EntryPreparation.ClientPending}.");
                return;
            }
            admittedAt??=now;
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
                var visuals=actors.Select(actor=>runtime.FindActorVisual(actor.id) as PonyVisual).ToArray();
                if(!runtime.LevelPresentationReady || runtime.IsLevelPreparing ||
                    visuals.Any(visual=>visual==null || !visual.gameObject.activeInHierarchy))
                {
                    if(now-admittedAt.Value>=PresentationTimeoutSeconds)
                        Finish(false,"Участники подключены, но представление локации или персонажей не готово.");
                    return;
                }
                for(int i=0;i<actors.Length;i++)
                {
                    string error=VerifyRenderedAppearance(actors[i],visuals[i]);
                    if(error!=null) {Finish(false,error);return;}
                }
                NetworkFrameCapture.Save(runtime.WorldCamera,Path.ChangeExtension(runtime.Options.Report,"png"));
            }
            session.Saves.SaveCharacter(session.LocalActor);
            var loaded=session.Saves.LoadCharacter();
            bool saved=loaded.hair==session.LocalActor.hair && PonyAppearance.Snapshot(loaded).Equals(PonyAppearance.Snapshot(session.LocalActor));
            Finish(saved,saved?"Все участники получили отдельные формы головы/гривы/глаз, пять цветов и второй цвет; старый хост сохранён, локальный профиль совпадает. При frames проверены реальные меши и цвета каждого персонажа.":"Локальное сохранение потеряло внешность.");
        }
        private static string VerifyRenderedAppearance(ActorState actor,PonyVisual visual)
        {
            visual.DrawPose(actor.poseTime);
            var key=PonyAppearance.Snapshot(actor);
            var parts=PonyRenderSelection.Frames(key,visual.DrawnClip,visual.DrawnExpression)[visual.DrawnFrame];
            var renderers=visual.GetComponentsInChildren<MeshRenderer>().Where(renderer=>
                renderer.sortingOrder>=WorldVisualOrderPolicy.ActorPartOrder &&
                renderer.sortingOrder<WorldVisualOrderPolicy.ActorPartOrder+parts.Length)
                .OrderBy(renderer=>renderer.sortingOrder).ToArray();
            if(renderers.Length!=parts.Length) return "Сетевая внешность не применена к графике: "+actor.character;
            var block=new MaterialPropertyBlock();
            for(int i=0;i<parts.Length;i++)
            {
                var part=parts[i];var renderer=renderers[i];
                float opacity=PonyMagicRenderRoles.Contains(part)?visual.DrawnMagicActivity.Opacity:1;
                if(renderer.sortingOrder!=WorldVisualOrderPolicy.ActorPartOrder+i ||
                    renderer.GetComponent<MeshFilter>().sharedMesh!=part.mesh ||
                    renderer.enabled!=(part.visible && opacity>0))
                    return "Сетевой меш или видимость не совпали: "+actor.character+"/"+part.slot;
                renderer.GetPropertyBlock(block);
                var expected=PonyAppearance.Tint(part.slot,key);expected.a*=opacity;
                if(!block.HasColor("_Color") || block.GetColor("_Color")!=expected)
                    return "Сетевой цвет не применён: "+actor.character+"/"+part.slot;
            }
            return null;
        }
        private CharacterAppearance Expected(int index)=>new()
        {version=CharacterAppearance.CurrentVersion,headStyleId=forcedHeadId??PonyHeadCatalog.Styles[index%PonyHeadCatalog.Styles.Count].id,
            hairStyle=6-index,eyeStyle=7-index,fur=0xA3A3A3+index*0x010101,hair=0x225588+index*0x080005,secondaryHair=0xEE2288-index*0x001100,
            eyes=0x22AA44+index*0x000022,magic=0xFFAA00-index*0x110000,secondaryHairEnabled=index%2==1};
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {
                success=success,detail=detail,forcedHeadId=forcedHeadId,connectedPeers=connectedPeers,
                admissionSeconds=admittedAt.HasValue?admittedAt.Value-startedAt:-1,
                elapsedSeconds=Time.realtimeSinceStartupAsDouble-startedAt},true));
            StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(3);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report
        {
            public bool success;
            public string detail,forcedHeadId;
            public int connectedPeers;
            public double admissionSeconds,elapsedSeconds;
        }
    }
}
