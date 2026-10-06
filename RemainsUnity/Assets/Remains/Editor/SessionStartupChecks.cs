using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Persistence;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class SessionStartupChecks
    {
        public static void Run()
        {
            CheckIntentAt(Path.Combine(Path.GetTempPath(),"remains-startup-"+Guid.NewGuid().ToString("N")));
            CheckPresentation();
            Debug.Log("Запуск мира: PASS — явные Continue/NewWorld/ChooseCopies, точная выбранная копия и ветви/модовые поля, согласование контента отдельно от списка копий. Сетевой порядок согласований проверяется native CharacterSelectionProbe.");
        }
        // Also callable by a managed harness without creating Unity objects.
        public static void CheckIntentAt(string directory)
        {
            var world=new WorldState {seed=42,revision=7};WorldHistory.BeginContinuation(world);world.checkpointSequence=2;
            WorldHistory.BeginContinuation(world);world.checkpointSequence=3;
            world.weaponContent=Manifest("saved-pack");
            world.extensions=new Dictionary<string,JToken> {{"startup-mod",new JObject {{"nested",new JArray("сохранить",decimal.MaxValue)}}}};
            world.containers.Add(new ContainerState {id="container:startup",items=new List<ItemStack> {
                new ItemStack {id="item:startup",definition="p10",count=3,extensions=new Dictionary<string,JToken> {{"item-mod",true}}}}});
            world.completedTransactions.Add("claim:completed");
            var frozen=RecoverySnapshot.FromWorld(world);var intent=SessionStartupIntent.Continue(frozen);
            Require(intent.Mode==SessionStartupMode.ContinueSelected && ReferenceEquals(intent.Selected,frozen),"Continue не заморозил payload");
            intent.RequireLoad(frozen,false);
            Require(intent.StartsOnJoin(true) && !intent.StartsOnJoin(false),"Continue зависит от remote join вместо собственного host join");
            Require(intent.ReservesHostCharacter(true,"pony-host","pony-host") &&
                !intent.ReservesHostCharacter(true,"pony-host","pony-guest") &&
                !intent.ReservesHostCharacter(false,"pony-host","pony-host"),"Direct startup не резервирует точный профиль localhost/блокирует другой профиль или dedicated client");
            var branch=world.Copy();WorldHistory.BeginContinuation(branch);branch.containers[0].items.Clear();branch.revision++;
            var other=RecoverySnapshot.FromWorld(branch);
            Require(WorldHistory.Compare(branch,other.Id,world,frozen.Id)==WorldRelation.Newer,"Fixture продолжения не образует отдельную ветвь");
            Reject(()=>intent.RequireLoad(other,false),"Другие bytes/branch заменили выбранную копию");
            Reject(()=>intent.RequireLoad(new RecoverySnapshot(frozen.Json+" "),false),"Семантически равный, но иной полный payload принят");
            Reject(()=>intent.RequireLoad(frozen,true),"Continue создал новый мир");
            Reject(()=>intent.RequireLoad(null,false),"Continue потерял выбранную копию");
            Reject(()=>SessionStartupIntent.Continue(null),"Пустой Continue принят");
            var divergent=world.Copy();divergent.branchId=Guid.NewGuid().ToString("N");divergent.containers[0].items[0].count=1;
            var divergentCopy=RecoverySnapshot.FromWorld(divergent);
            Require(WorldHistory.Compare(divergent,divergentCopy.Id,world,frozen.Id)==WorldRelation.Divergent,"Fixture не является разошедшейся ветвью");
            var store=new SaveStore(directory);store.SaveRecovery(frozen);store.SaveRecovery(other);store.SaveRecovery(divergentCopy);
            Reject(()=>intent.RequireLoad(store.SelectedSnapshot(),false),"Повторное чтение активного сейва подменяет frozen intent");
            Require(intent.Selected.Id==frozen.Id && intent.Selected.Json==frozen.Json,"Смена выбранного файла изменила intent");
            store.SelectContinuation(intent.Selected);
            Require(store.SelectedSnapshot().Json==frozen.Json && store.Continuations().Any(copy=>copy.Json==other.Json) &&
                store.Continuations().Any(copy=>copy.Json==divergentCopy.Json),"Выбор frozen copy потерял продолжение/разошедшуюся ветвь");
            var live=frozen.CreateLiveWorld();live.seed=-1;live.containers[0].items.Clear();
            var restored=intent.Selected.CreateLiveWorld();
            Require(restored.seed==42 && restored.containers[0].items.Single().count==3 &&
                restored.containers[0].items[0].extensions["item-mod"].Value<bool>() &&
                restored.extensions["startup-mod"]["nested"][1].Value<decimal>()==decimal.MaxValue &&
                restored.completedTransactions.Contains("claim:completed") && restored.branchId==world.branchId &&
                restored.ancestors.Single().branchId==world.ancestors.Single().branchId && restored.ancestors.Single().sequence==2,
                "Live edits потеряли full copy/receipt/unknown extensions/history");
            Require(ContentComparison.Compare(restored.weaponContent,Manifest("current-pack")).Count>0 &&
                ContentComparison.Compare(restored.weaponContent,Manifest("saved-pack")).Count==0,
                "Intent перезаписал сохранённый манифест/отключил различие контента");
            var fresh=SessionStartupIntent.NewWorld();fresh.RequireLoad(null,true);
            Require(fresh.StartsOnJoin(true) && !fresh.StartsOnJoin(false) && fresh.Selected==null,"Новый мир зависит от remote join");
            Require(fresh.ReservesHostCharacter(true,"pony-host","pony-host"),"NewWorld не резервирует профиль localhost");
            Reject(()=>fresh.RequireLoad(frozen,false),"NewWorld заменён копией");Reject(()=>fresh.RequireLoad(null,false),"NewWorld не создаёт мир");
            var choose=SessionStartupIntent.ChooseCopies();
            Require(choose.ChoosesCopies && !choose.StartsOnJoin(true) && !choose.StartsOnJoin(false),"ChooseCopies автоматически запускается от join");
            Require(!choose.ReservesHostCharacter(true,"pony-host","pony-host"),"Direct reservation изменяет manual copy semantics");
            choose.RequireLoad(frozen,false);choose.RequireLoad(other,false);choose.RequireLoad(null,true);
            Reject(()=>choose.RequireLoad(null,false),"ChooseCopies принимает несогласованный null");
            Reject(()=>choose.RequireLoad(other,true),"ChooseCopies одновременно создаёт новый и загружает старый мир");
        }
        public static void CheckPresentation()
        {
            var parent=new GameObject("Проверка экранов запуска",typeof(RectTransform));
            try
            {
                var view=WorldLobbyView.Create(parent.transform,false);
                Require(!view.Contents.activeSelf && !view.StartGame.gameObject.activeInHierarchy &&
                    !view.NewWorld.gameObject.activeInHierarchy && view.Choices.All(button=>!button.gameObject.activeInHierarchy),
                    "Согласование ordinary Continue показывает список/Start/NewWorld");
                view.ShowWaiting("Хост сравнивает содержимое выбранного мира.","Ожидание решения");
                Require(view.Instruction.gameObject.activeInHierarchy && view.Disconnect.gameObject.activeInHierarchy &&
                    !view.StartGame.gameObject.activeInHierarchy && !view.NewWorld.gameObject.activeInHierarchy &&
                    view.Choices.All(button=>!button.gameObject.activeInHierarchy),"Remote content wait показывает copy picker");
                UnityEngine.Object.DestroyImmediate(view.gameObject);
                view=WorldLobbyView.Create(parent.transform);
                Require(view.Contents.activeSelf && view.StartGame.gameObject.activeInHierarchy && view.NewWorld.gameObject.activeInHierarchy &&
                    view.Choices.All(button=>button.gameObject.activeInHierarchy),"Явный ChooseCopies потерял свои действия");
            }
            finally {UnityEngine.Object.DestroyImmediate(parent);}
        }
        private static ContentManifest Manifest(string hash)=>new() {schema=1,scope="startup-contract",fingerprint=hash,
            packs=new List<ContentPackRecord> {new() {id="pack:startup",version="1",fingerprint=hash}},
            gameplayAssets=new List<ContentAssetRecord> {new() {id="resource:startup",fingerprint=hash}}};
        private static void Reject(Action action,string reason)
        {bool refused=false;try {action();} catch(Exception error) when(error is InvalidDataException || error is ArgumentNullException) {refused=true;}Require(refused,reason);}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Запуск мира: "+reason);}
    }
}
