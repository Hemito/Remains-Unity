using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyHeadAppearanceChecks
    {
        private static void Require(bool value,string detail)
        {if(!value) throw new InvalidOperationException("Формы головы: "+detail);}
        public static void Run()
        {
            PonyAppearance.Prepare();
            var legacy=StateJson.FromJson<CharacterAppearance>("{\"version\":1,\"hairStyle\":5,\"eyeStyle\":6,\"fur\":1122867,\"hair\":4478310,\"secondaryHair\":7833753,\"eyes\":11189196,\"magic\":14544639,\"secondaryHairEnabled\":true,\"mod.headHint\":{\"id\":\"external.head\",\"value\":1.234567890123456789}}");
            var extensions=legacy.extensions;var unknown=extensions["mod.headHint"].DeepClone();
            legacy.Validate("проверка миграции v1");
            Require(legacy.version==CharacterAppearance.CurrentVersion && legacy.headStyleId==CharacterAppearance.OriginalHeadStyleId,"миграция v1 изменила исходную голову или не обновила версию");
            Require(legacy.hairStyle==5 && legacy.eyeStyle==6 && legacy.fur==1122867 && legacy.hair==4478310 && legacy.secondaryHair==7833753 &&
                legacy.eyes==11189196 && legacy.magic==14544639 && legacy.secondaryHairEnabled,"миграция изменила старые параметры внешности");
            Require(ReferenceEquals(extensions,legacy.extensions) && JToken.DeepEquals(unknown,legacy.extensions["mod.headHint"]),"миграция потеряла неизвестные данные мода");
            var copy=StateJson.Copy(legacy);
            Require(copy.version==CharacterAppearance.CurrentVersion && copy.headStyleId==CharacterAppearance.OriginalHeadStyleId && JToken.DeepEquals(unknown,copy.extensions["mod.headHint"]),"StateJson roundtrip потерял миграцию или дополнительные данные");
            Reject(new CharacterAppearance {version=CharacterAppearance.CurrentVersion+1});
            Reject(new CharacterAppearance {version=1,headStyleId="pony.head.external"});
            Reject(new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId=null});
            var invalid=new ActorState {id="проверка неизвестной головы",appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId="pony.head.missing"}};
            bool rejected=false;
            try {PonyAppearance.Snapshot(invalid);} catch(InvalidOperationException error) {rejected=error.Message.Contains(invalid.id) && error.Message.Contains(invalid.appearance.headStyleId);}
            Require(rejected && invalid.appearance.headStyleId=="pony.head.missing","неизвестная голова подменена или диагностика не содержит actor/head");

            PonyRenderSelection.Prepare();
            CheckExplicitPreparation();
            var asset=Resources.Load<PonyRenderAsset>("Original/Pony/render");
            PonyAppearanceKey? previous=null;int frames=0;
            foreach(var style in PonyHeadCatalog.Styles)
            {
                var actor=new ActorState {id="head-contract:"+style.id,appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId=style.id}};
                var key=PonyAppearance.Snapshot(actor);var plan=PonyRenderSelection.PrepareEquipment(actor);
                Require(plan.MatchesShape(key),"план не узнаёт собственную голову: "+style.id);
                if(previous.HasValue) Require(!previous.Value.Equals(key) && !plan.MatchesShape(previous.Value),"разные ID головы совпали в ключе/плане");
                previous=key;
                var roundtrip=StateJson.Copy(actor);
                Require(roundtrip.appearance.headStyleId==style.id && PonyAppearance.Snapshot(roundtrip).Equals(key),"копия сетевого/сохранённого актёра потеряла голову: "+style.id);
                foreach(var clip in asset.Clips)
                {
                    var bare=PonyRenderSelection.Frames(key,clip.id);var composite=plan.Frames(clip.id);
                    for(int f=0;f<bare.Length;f++)
                    {
                        Require(bare[f].SequenceEqual(composite[f]),$"мир и общий план выбрали разные части: head={style.id}, clip={clip.id}, frame={f}");
                        var head=bare[f].Single(part=>part.sourceShape==PonyHeadRenderResolver.SourceShape);
                        var source=clip.frames[f].parts.Single(part=>part.sourceShape==PonyHeadRenderResolver.SourceShape);
                        var expected=style.id==CharacterAppearance.OriginalHeadStyleId?source:
                            asset.HeadStyles.Single(value=>value.id==style.id).clips.Single(value=>value.id==clip.id).frames[f].parts[0];
                        Require(ReferenceEquals(head,expected) && head.sourceIndex==source.sourceIndex && head.drawOrder==source.drawOrder,
                            $"голова/слой не соответствует подготовленному ID: head={style.id}, clip={clip.id}, frame={f}");
                        frames++;
                    }
                }
                Require(ReferenceEquals(plan.PortraitFrame,plan.Frames("idle")[0]),"портрет не использует общий подготовленный кадр");
            }
            Debug.Log($"Формы головы: PASS — миграция v1→v{CharacterAppearance.CurrentVersion}, неизвестные данные StateJson, {PonyHeadCatalog.Styles.Count} ID × {frames/PonyHeadCatalog.Styles.Count} кадров, общий выбор мира/портрета, без подмены неизвестных ID.");
        }
        private static void CheckExplicitPreparation()
        {
            foreach(var style in PonyHeadCatalog.Styles)
            {
                if(style.id==CharacterAppearance.OriginalHeadStyleId) continue;
                for(int hair=1;hair<=PonyAnimationData.Library.hairStyles;hair++)
                for(int eye=1;eye<=PonyAnimationData.Library.eyeStyles;eye++)
                for(int secondary=0;secondary<2;secondary++)
                {
                    var actor=new ActorState {id="head-explicit-prepare",appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,
                        headStyleId=style.id,hairStyle=hair,eyeStyle=eye,secondaryHairEnabled=secondary==1}};
                    var key=PonyAppearance.Snapshot(actor);int count=PonyRenderSelection.PreparedSelectionCount;bool missing=false;
                    try {PonyRenderSelection.Frames(key,"idle");}
                    catch(InvalidOperationException error) {missing=error.Message.Contains(style.id) && error.Message.Contains("PrepareEquipment");}
                    Require(PonyRenderSelection.PreparedSelectionCount==count,"Frames/Get подготовил сочетание во время чтения");
                    if(!missing) continue;
                    PonyRenderSelection.PrepareEquipment(actor);
                    Require(PonyRenderSelection.PreparedSelectionCount==count+1 && PonyRenderSelection.Frames(key,"idle").Length>0,
                        "явное событие не подготовило ровно одно выбранное сочетание");
                    Require(PonyRenderSelection.LastShapePrepareMilliseconds>=0,"некорректное время подготовки сочетания");
                    return;
                }
            }
        }
        private static void Reject(CharacterAppearance appearance)
        {
            try {appearance.Validate("проверка недопустимой версии/головы");}
            catch(InvalidOperationException) {return;}
            throw new InvalidOperationException("Формы головы: принята недопустимая версия/голова.");
        }
    }
}
