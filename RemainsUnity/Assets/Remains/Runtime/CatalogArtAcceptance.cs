using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Explicit background diagnostic only: operates the saved game scene, not a reconstructed test scene.
    [DefaultExecutionOrder(20000)]
    public sealed class CatalogArtAcceptance : MonoBehaviour
    {
        private const string Weapon="weapon:p10mm:grade:1",Ammo="item:p10";
        private const string WeaponItem="vector-check-weapon",AmmoItem="vector-check-ammo";
        private PrototypeRuntime runtime;
        private InteractionVisuals visuals;
        private WeaponAsset asset;
        private WeaponDefinition definition;
        private Vector2 aim;
        private bool fire,reload,finished,freezeShot;
        private float deadline;
        private int shots;
        private string stage="запуск",reportPath;
        private readonly List<Capture> captures=new();
        private ActorState Actor=>runtime.Session.LocalActor;
        [Serializable] private sealed class Capture
        {
            public string phase,path,sprite,key;public int frame,width,height;
            public float pixelsPerUnit,muzzleError;
            public Vector3 position,boundsCenter,boundsSize;
            public Vector2 pivot;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1,shots,roundsBefore,roundsAfterShot,roundsAfterReload,reserveBefore,reserveAfterReload;
            public bool success,vectorEnabled,heldObserved,shotObserved,reloadObserved,dropObserved;
            public string detail,stage,weapon=Weapon,ammo=Ammo,drop,world,limitation=
                "Диагностический одиночный Windows host, сохранённая игровая сцена, настоящий выстрел/перезарядка/бросок. Не проверяет LAN и подбор. Сравнение PNG выполняется отдельно.";
            public Capture[] captures;
        }
        private readonly Report report=new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-vectorStage2Check")<0) return;
            // Find is restricted to this explicitly requested diagnostic, never ordinary scene composition.
            var owner=FindFirstObjectByType<PrototypeRuntime>();
            if(owner==null) {Debug.LogError("Приёмка графики: нет runtime в сохранённой сцене.");Application.Quit(1);return;}
            var probe=owner.gameObject.AddComponent<CatalogArtAcceptance>();probe.runtime=owner;
        }
        private IEnumerator Start()
        {
            var driver=Run();
            while(true)
            {
                object next;
                try {if(!driver.MoveNext()) yield break;next=driver.Current;}
                catch(Exception error) {Finish(false,error.ToString());yield break;}
                yield return next;
            }
        }
        private IEnumerator Run()
        {
            var options=runtime.Options;reportPath=options.Report;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-CaptureFrames")>=0,"Нужен -CaptureFrames.");
            Require(runtime.enabled && runtime.Session!=null && options.Role==null && options.Smoke==null,
                "Нужна обычная сохранённая сцена без --role и --smoke; diagnostic сам запускает host.");
            Require(!string.IsNullOrWhiteSpace(options.SaveDirectory) && !string.IsNullOrWhiteSpace(reportPath),"Нужны изолированные --save-dir и --report.");
            Require(runtime.Session.Saves.SelectedSnapshot()==null && runtime.Session.Saves.ReadCharacterSource()==null,
                "Приёмка не заменяет существующий мир или персонажа: нужен новый save-dir.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            deadline=Time.realtimeSinceStartup+180;
            report.vectorEnabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-noVectorArt")<0;
            Application.targetFrameRate=60;QualitySettings.vSyncCount=0;Time.captureDeltaTime=1f/60;
            asset=GameAssets.Load<WeaponAsset>("Content/weapons/Original/p10mm-1");
            Require(asset!=null && asset.Id==Weapon && asset.ShotAnimation!=null && asset.ReloadAnimation!=null,"Не подготовлено оригинальное оружие p10mm-1.");
            definition=asset.CreateDefinition();report.roundsBefore=definition.magazineSize;report.reserveBefore=24;
            // A profile fixture goes through the ordinary join/migration path; no grant is forged after joining.
            var profile=new ActorState {id="player:"+options.Character,character=options.Character,player=true,level="hub",
                controlStateVersion=ActorSessionRules.ControlStateVersion,loadoutVersion=1,weaponInventoryVersion=1,weapon=Weapon,equippedWeaponItem=WeaponItem,
                appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion}};
            profile.inventory.Add(new ItemStack {id=WeaponItem,definition=Weapon,count=1,weaponState=new WeaponItemState {rounds=definition.magazineSize}});
            profile.inventory.Add(new ItemStack {id=AmmoItem,definition=Ammo,count=report.reserveBefore});
            runtime.Session.Saves.SaveCharacter(profile);
            visuals=FindFirstObjectByType<InteractionVisuals>();
            Require(visuals!=null && visuals.ReloadAnimation!=null,"Нет игровых сервисов изображения оружия.");
            runtime.CommandSource=Command;
            runtime.Session.AttackReceived+=ObserveAttack;
            runtime.Session.UseLobby=false;runtime.Session.StartHost();
            stage="ожидание игрового мира";
            while(Actor==null || runtime.IsLevelPreparing || visuals.FindWeapon(Actor.id)?.Artwork.sprite==null) {CheckDeadline();yield return null;}
            report.world=runtime.Session.World.worldId;
            Require(Actor.weapon==Weapon && WeaponInventory.Selected(Actor)?.id==WeaponItem,"Join изменил выбранное оружие каталога.");
            // Warm both deferred clips before measuring the real transition; no missing-frame substitution is accepted.
            asset.ShotAnimation.Preload();asset.ReloadAnimation.Preload();
            for(int i=0;i<asset.ShotAnimation.FrameCount;i++) _=asset.ShotAnimation.LoadFrameNow(i);
            for(int i=0;i<asset.ReloadAnimation.FrameCount;i++) _=asset.ReloadAnimation.LoadFrameNow(i);
            aim=WeaponPose.RangedAnchor(Actor)+new Vector2(4,.15f);
            for(int i=0;i<60;i++) {CheckDeadline();yield return null;}
            stage="кадр удержания";
            runtime.Session.Pause.SetManual(true);
            while(!runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            Time.timeScale=0;yield return null;yield return null;
            var view=visuals.FindWeapon(Actor.id);
            Require(view!=null && view.Artwork.sprite==asset.Presentation.ResolveRestSprite(),"Кадр покоя не связан с ключом оригинального оружия.");
            CaptureFrame("held",view.Artwork,asset.Presentation.RestReference.Key,-1);report.heldObserved=true;
            Time.timeScale=1;runtime.Session.Pause.SetManual(false);
            while(runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            for(int i=0;i<4;i++) yield return null;
            stage="выстрел";freezeShot=true;fire=true;
            while(shots==0) {CheckDeadline();yield return null;}
            while(!runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            yield return null;yield return null;
            view=visuals.FindWeapon(Actor.id);
            int shotFrame=-1;
            for(int i=0;i<asset.ShotAnimation.FrameCount;i++) if(asset.ShotAnimation.LoadFrameNow(i)==view.Artwork.sprite) {shotFrame=i;break;}
            Require(shotFrame>=0,"Фактический спрайт выстрела не является кадром оригинальной анимации.");
            CaptureFrame("shot",view.Artwork,null,shotFrame);report.shotObserved=true;
            report.roundsAfterShot=WeaponInventory.Selected(Actor).weaponState.rounds;
            Require(shots==1 && report.roundsAfterShot==report.roundsBefore-1,"Нет единственного подтверждённого выстрела с расходом патрона.");
            Time.timeScale=1;runtime.Session.Pause.SetManual(false);
            while(runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            for(int i=0;i<30;i++) yield return null;
            stage="перезарядка";reload=true;
            while(!runtime.Combat.Reload.Active) {CheckDeadline();yield return null;}
            while(runtime.Combat.Reload.Remaining>definition.reloadSeconds*.65f) {CheckDeadline();yield return null;}
            Time.timeScale=0;runtime.Session.Pause.SetManual(true);
            while(!runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            yield return null;yield return null;
            Require(visuals.ReloadAnimation.TryGet(Actor,out var reloadState),"Нет фактического состояния перезарядки.");
            view=visuals.FindWeapon(Actor.id);
            Require(view.Artwork.sprite==asset.ReloadAnimation.LoadFrameNow(reloadState.Frame),"Renderer не показывает ожидаемый кадр перезарядки.");
            CaptureFrame("reload",view.Artwork,null,reloadState.Frame);report.reloadObserved=true;
            Time.timeScale=1;runtime.Session.Pause.SetManual(false);
            while(runtime.Combat.Reload.Active || runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            report.roundsAfterReload=WeaponInventory.Selected(Actor).weaponState.rounds;
            report.reserveAfterReload=InventoryJournal.Count(Actor,Ammo);
            Require(report.roundsAfterReload==report.roundsBefore && report.reserveAfterReload==report.reserveBefore-1,"Перезарядка не перенесла ровно один патрон из запаса.");
            stage="бросок предмета каталога";
            aim=Actor.Position+new Vector2(.5f,.4f);
            Require(runtime.ItemActions.ActivateItem(AmmoItem,aim),"Не выбран предмет каталога для броска.");
            for(int i=0;i<12;i++) yield return null;
            Require(runtime.ItemActions.TryThrow(aim),"Игровой сервис отклонил бросок предмета каталога.");
            ContainerState drop=null;
            while(drop==null || visuals.FindContainer(drop.id)?.Artwork.sprite==null)
            {
                CheckDeadline();drop=runtime.Session.World.containers.FirstOrDefault(value=>value.presentationItem==Ammo && value.items.Count>0);
                yield return null;
            }
            report.drop=drop.id;
            for(int i=0;i<60;i++) {CheckDeadline();yield return null;}
            runtime.Session.Pause.SetManual(true);
            while(!runtime.Session.Pause.IsPaused(Actor.level)) {CheckDeadline();yield return null;}
            Time.timeScale=0;yield return null;yield return null;
            var prop=visuals.FindContainer(drop.id);var shape=DroppedItemShapeCatalog.Require(Ammo);
            Require(prop!=null && prop.Artwork.sprite==shape.sprite,"Брошенный предмет не связан с ключом подготовленной формы.");
            CaptureFrame("dropped",prop.Artwork,shape.Reference.Key,-1);report.dropObserved=true;
            report.shots=shots;Finish(true,"Оригинальное оружие: удержание, один выстрел и завершённая перезарядка; предмет каталога брошен и виден.");
        }
        private PlayerCommand Command()
        {
            var command=new PlayerCommand {hasAim=true,aim=aim,fire=fire,reload=reload};fire=reload=false;return command;
        }
        private void ObserveAttack(AttackEvent attack)
        {
            if(attack.attacker!=Actor?.id || attack.weapon!=Weapon) return;
            shots++;
            if(freezeShot) {freezeShot=false;Time.timeScale=0;runtime.Session.Pause.SetManual(true);}
        }
        private void CaptureFrame(string phase,SpriteRenderer renderer,string key,int frame)
        {
            Require(renderer!=null && renderer.gameObject.activeInHierarchy && renderer.enabled && renderer.sprite!=null,"Нет видимой графики: "+phase);
            var camera=runtime.WorldCamera;var bounds=renderer.bounds;
            var viewport=camera.WorldToViewportPoint(bounds.center);
            Require(viewport.z>0 && viewport.x>0 && viewport.x<1 && viewport.y>0 && viewport.y<1,"Изображение за пределами камеры: "+phase+", "+viewport);
            float muzzleError=0;
            if(phase!="dropped")
            {
                var weapon=visuals.FindWeapon(Actor.id);muzzleError=Vector2.Distance(weapon.Muzzle.position,WeaponPose.Muzzle(Actor));
                Require(muzzleError<.0001f,"Графическое дуло расходится с игровым: "+phase+", "+muzzleError);
            }
            string path=reportPath+"."+phase+".png";
            NetworkFrameCapture.Save(camera,path,null,1920,1080);
            captures.Add(new Capture {phase=phase,path=path,key=key,frame=frame,width=1920,height=1080,sprite=renderer.sprite.name,
                pixelsPerUnit=renderer.sprite.pixelsPerUnit,pivot=renderer.sprite.pivot,muzzleError=muzzleError,
                position=renderer.transform.position,boundsCenter=bounds.center,boundsSize=bounds.size});
        }
        private void CheckDeadline()
        {Require(Time.realtimeSinceStartup<deadline,"Таймаут приёмки: "+stage);}
        private static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Приёмка графики каталога: "+reason);}
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;Time.timeScale=1;Time.captureDeltaTime=0;
            report.success=success;report.detail=detail;report.stage=stage;report.shots=shots;report.captures=captures.ToArray();
            if(!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath,StateJson.ToJson(report,true));
            if(success) Debug.Log("Приёмка графики каталога: PASS; "+detail);else Debug.LogError(detail);
            Application.Quit(success?0:1);
        }
    }
}
