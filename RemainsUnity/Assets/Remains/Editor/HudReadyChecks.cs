using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Remains.Editor
{
    // The real ready callback must publish personal HUD before the next Update.
    // No sockets, saved profiles, HUD Update/Refresh invocation or renderer fixture.
    public static class HudReadyChecks
    {
        public static void Run()
        {
            var owner=new GameObject("Неактивная сессия проверки готовности HUD");owner.SetActive(false);
            var hudObject=new GameObject("Проверка первого готового HUD",typeof(RectTransform));
            var cameraObject=new GameObject("Камера первого готового HUD",typeof(Camera));
            var lightObject=new GameObject("Свет первого готового HUD",typeof(RoomLighting));
            var previousEvents=new HashSet<EventSystem>(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None));
            var beforeFG=Shader.GetGlobalTexture("_WorldLightMap");var beforeBG=Shader.GetGlobalTexture("_BackgroundLightMap");
            var beforeBounds=Shader.GetGlobalVector("_WorldLightBounds");float beforeEnabled=Shader.GetGlobalFloat("_WorldLightEnabled");
            RoomPresentationGate gate=null;PrototypeHud hud=null;
            try
            {
                var session=owner.AddComponent<CoopSession>();session.ConfigureRules(WeaponCatalog.Rules);
                var actor=new ActorState {id="hud-ready-player",owner="hud-ready-owner",character="Первый готовый кадр",
                    level="hub",player=true,connected=true,weapon="pistol",health=73,mana=42};
                PlayerStarterInventory.Give(actor);
                var world=new WorldState();world.actors.Add(actor);
                // The coordinator has no public detached-world binding. Match the existing
                // Editor fixture pattern; production admission and saves are not invoked.
                typeof(CoopSession).GetProperty(nameof(CoopSession.World)).SetValue(session,world);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalActorId)).SetValue(session,actor.id);
                typeof(CoopSession).GetProperty(nameof(CoopSession.LocalOwner)).SetValue(session,actor.owner);
                var camera=cameraObject.GetComponent<Camera>();const int visibleLayers=31;camera.cullingMask=visibleLayers;
                hud=hudObject.AddComponent<PrototypeHud>();hud.Initialize(session,worldCamera:camera);
                var view=hudObject.GetComponentInChildren<CombatHudView>(true);
                var sessionView=hudObject.GetComponentInChildren<SessionHudView>(true);
                Require(view!=null && sessionView!=null,"нет реальных сохранённых HUD views");
                // Start with the real view hidden, as it was in the previous pending
                // runtime frame. Do not invoke HUD.Update to manufacture the transition.
                view.Vitals.SetActive(false);view.Loadout.SetActive(false);
                int nodes=hudObject.GetComponentsInChildren<Transform>(true).Length;
                var light=lightObject.GetComponent<RoomLighting>();
                var room=new RoomLayout {id="hud-ready-room",left=0,right=2,bottom=0,top=2,
                    platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),artwork=Array.Empty<RoomArtwork>()};
                var profile=new RoomLightProfile {ambient=new Color(.1f,.12f,.15f),lights=Array.Empty<RoomLight>()};
                gate=new RoomPresentationGate(camera);gate.SetContext(true,room);gate.Bind(light);gate.SetHud(hud);
                Require(hud.RoomPresentationPending && hud.RoomLoadingVisible && camera.cullingMask==0 &&
                    !view.Vitals.activeInHierarchy && !view.Loadout.activeInHierarchy && !sessionView.gameObject.activeInHierarchy,
                    "pending оставил часть игрового HUD или камеру видимой");
                actor.health=61;actor.mana=37;
                WeaponAmmunition.Magazine(actor,"pistol").rounds=7;
                // Initialize raises the actual public readiness event synchronously.
                // Without the event refresh fix, this assertion fails before any HUD Update.
                light.Initialize(room,profile);
                Require(gate.IsReady && !hud.RoomPresentationPending && !hud.RoomLoadingVisible && camera.cullingMask==visibleLayers,
                    "готовность не опубликовала согласованные HUD/камеру");
                Require(view.Vitals.activeInHierarchy && view.Loadout.activeInHierarchy && sessionView.gameObject.activeInHierarchy,
                    "первый готовый кадр требует следующего Update для личного HUD");
                Require(view.CharacterName.text==actor.character && view.HealthText.text=="61/100" && view.ManaText.text=="Мана  37 / 100" &&
                    Mathf.Approximately(view.Health.fillAmount,.61f) && Mathf.Approximately(view.Mana.fillAmount,.37f) &&
                    view.Ammunition.text.StartsWith("7 / 12    Запас: 36",StringComparison.Ordinal),
                    "готовый HUD показывает прежние HP/ману/имя/магазин вместо текущего LocalActor");
                Require(sessionView.Status.text=="Хаб · игроков: 1","название сессии потеряло источник LocalActor");
                Require(hudObject.GetComponentsInChildren<Transform>(true).Length==nodes,"ready event пересоздал интерфейс");

                hud.ToggleInventory();
                Require(hud.InventoryOpen && !view.Vitals.activeInHierarchy && !view.Loadout.activeInHierarchy &&
                    !sessionView.gameObject.activeInHierarchy,"event открытия инвентаря не применил общий visibility predicate");
                hud.ToggleInventory();
                Require(!hud.InventoryOpen && view.Vitals.activeInHierarchy && view.Loadout.activeInHierarchy,
                    "event закрытия инвентаря не вернул личный HUD");

                light.Release();
                Require(!view.Vitals.activeInHierarchy && !view.Loadout.activeInHierarchy && camera.cullingMask==0,
                    "новая подготовка не скрыла HUD/камеру через actual readiness event");
                hud.PauseMenu.Toggle();
                Require(hud.PauseMenu.IsOpen && hud.RoomPresentationPending,"fixture не открыла Escape во время подготовки");
                light.Initialize(room,profile);
                Require(gate.IsReady && camera.cullingMask==visibleLayers && hud.PauseMenu.IsOpen &&
                    !view.Vitals.activeInHierarchy && !view.Loadout.activeInHierarchy && !sessionView.gameObject.activeInHierarchy,
                    "ready event показал игровой HUD поверх открытого Escape");

                light.Release();hud.PauseMenu.Toggle();
                var energy=actor.inventory.Find(item=>item.definition=="energy");
                Require(WeaponInventory.Select(actor,energy.id,out var reason),"не выбран реальный экземпляр energy: "+reason);
                actor.health=1;actor.mana=19;
                light.Initialize(room,profile);
                Require(view.Vitals.activeInHierarchy && view.Loadout.activeInHierarchy && view.HealthText.text=="1/100" &&
                    view.ManaText.text=="Мана  19 / 100" && view.Ammunition.text.StartsWith("15 / 15    Запас: 45",StringComparison.Ordinal),
                    "повторная готовность не прочитала новый выбор/HP без следующего Update");

                // A failed event-driven UI refresh must not open the world camera.
                light.Release();actor.weapon="hud-ready-invalid-weapon";
                bool rejected=false;
                try {light.Initialize(room,profile);} catch(InvalidOperationException) {rejected=true;}
                Require(rejected && camera.cullingMask==0,"ошибка данных HUD открыла готовую камеру");
                actor.weapon="energy";
                Require(hudObject.GetComponentsInChildren<Transform>(true).Length==nodes,"повторные readiness events пересоздали интерфейс");

                // The owner must release its global Canvas/camera subscriptions,
                // including outlines whose renderer root lives outside the HUD.
                hud.ConfigureWorldOutline(camera,_=>default);
                var outline=hud.InteractionOutline;var outlineRenderers=outline.Renderers;
                Canvas.ForceUpdateCanvases();
                int placements=hud.InteractionHintPlacementEvaluationCount;
                gate.Dispose();gate=null;
                hud.Dispose();hud.Dispose();
                Require(!hud.enabled,"завершённый HUD продолжает получать Update");
                foreach(var renderer in outlineRenderers)
                    Require(renderer==null,"teardown HUD оставил внешний renderer контура");
                UnityEngine.Object.DestroyImmediate(hudObject);hudObject=null;
                Canvas.ForceUpdateCanvases();Canvas.ForceUpdateCanvases();
                Require(hud.InteractionHintPlacementEvaluationCount==placements,
                    "Canvas вызывает размещение подсказки после завершения/уничтожения владельца HUD");
                Debug.Log("Первый готовый HUD: PASS — actual readiness callback без Update публикует текущие имя/HP/ману/магазин вместе с камерой; инвентарь/Escape сохраняют видимость, повторная готовность читает новый выбор, ошибка UI оставляет камеру закрытой; owner teardown освобождает контур и Canvas callback до уничтожения UI.");
            }
            finally
            {
                gate?.Dispose();
                hud?.Dispose();
                if(hudObject!=null) UnityEngine.Object.DestroyImmediate(hudObject);
                UnityEngine.Object.DestroyImmediate(lightObject);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(owner);
                foreach(var events in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                    if(!previousEvents.Contains(events)) UnityEngine.Object.DestroyImmediate(events.gameObject);
                Shader.SetGlobalTexture("_WorldLightMap",beforeFG);Shader.SetGlobalTexture("_BackgroundLightMap",beforeBG);
                Shader.SetGlobalVector("_WorldLightBounds",beforeBounds);Shader.SetGlobalFloat("_WorldLightEnabled",beforeEnabled);
            }
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Первый готовый HUD: "+reason);}
    }
}
