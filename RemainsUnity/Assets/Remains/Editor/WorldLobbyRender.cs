using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class WorldLobbyRender
    {
        public static void Render()
        {
            CheckIdentity();
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1024,768),new Vector2Int(1920,1080)}) RenderAt(size.x,size.y);
        }
        public static void CheckIdentity()
        {
            var samples=IdentitySamples();
            var branches=samples.Select(copy=>copy.branchId).ToArray();
            var snapshots=samples.Select(copy=>copy.snapshot).ToArray();
            string Caption(WorldSummary copy,bool selected=false,WorldSummary reference=null) => WorldLobbyView.ChoiceText("Совместный завод","Один участник",copy.revision,
                WorldLobbyView.IdentityLabel(copy.branchId,branches),WorldLobbyView.IdentityLabel(copy.snapshot,snapshots),
                WorldLobbyView.RelationText(copy.CompareTo(reference??samples[0]),selected));
            void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Подписи копий лобби: "+reason);}
            Require(samples[1].CompareTo(samples[0])==WorldRelation.Divergent && samples[2].CompareTo(samples[0])==WorldRelation.Divergent,
                "fixture не содержит двух разошедшихся копий одного мира.");
            Require(samples[1].revision==samples[2].revision,"fixture разошедшихся копий имеет разные номера состояния.");
            Require(Caption(samples[1])!=Caption(samples[2]),"разошедшиеся копии одного участника с равным состоянием неразличимы.");
            Require(Caption(samples[1]).Contains(samples[1].branchId) && Caption(samples[2]).Contains(samples[2].branchId),
                "общий префикс ветвей скрывает отличающийся последний символ.");
            Require(Caption(samples[1]).Contains(samples[1].snapshot) && Caption(samples[2]).Contains(samples[2].snapshot),
                "общий префикс снимков скрывает отличающийся последний символ.");
            Require(samples[3].CompareTo(samples[1])==WorldRelation.Unknown && samples[3].branchId==samples[1].branchId &&
                samples[3].revision==samples[1].revision,"fixture одинаковой ветви/состояния не имеет независимый снимок.");
            Require(Caption(samples[3],false,samples[1])!=Caption(samples[1]) && Caption(samples[3],false,samples[1]).Contains("Связь копий не подтверждена"),
                "разные снимки одной ветви с равным состоянием скрыты или ошибочно упорядочены.");
            Require(WorldLobbyView.RelationText(WorldRelation.Identical,false)=="Такой же снимок, как выбранный" &&
                WorldLobbyView.RelationText(WorldRelation.Identical,true)=="Выбрана для запуска",
                "одинаковая копия другого участника выдана за выбранную кнопку.");
            Require(WorldLobbyView.IdentityLabel("0123456789abcdef",new[]{"0123456789abcdef","0123456789abcdef"})=="0123456789ab",
                "дубликат одного ID принудительно раскрывает весь идентификатор.");
            Require(WorldLobbyView.IdentityLabel(null,branches)=="нет данных","старая копия с отсутствующим ID получила выдуманную ветвь.");
            Require(Caption(samples[0],true).Split('\n').Length==4 && Caption(samples[0],true).Contains("состояние №"),
                "формат не содержит четыре строки и понятный номер состояния.");
            var continuation=new WorldSummary {worldId=samples[0].worldId,snapshot=new string('d',64),historyVersion=1,
                branchId=new string('e',32),sequence=0,revision=0,ancestors=new List<WorldAncestor>{new(){branchId=samples[0].branchId,sequence=samples[0].sequence}}};
            Require(continuation.CompareTo(samples[0])==WorldRelation.Newer && samples[0].CompareTo(continuation)==WorldRelation.Older,
                "отношения продолжения не читают существующее родство.");
            Require(WorldLobbyView.RelationText(WorldRelation.Newer,false)=="Продолжение выбранной копии" &&
                WorldLobbyView.RelationText(WorldRelation.Older,false)=="Предыдущая копия выбранного продолжения",
                "непонятное описание более ранней/поздней копии.");
            continuation.worldId=new string('f',32);
            Require(continuation.CompareTo(samples[0])==WorldRelation.DifferentWorld &&
                WorldLobbyView.RelationText(WorldRelation.DifferentWorld,false)=="Другой мир","другой мир назван продолжением.");
        }
        private static WorldSummary[] IdentitySamples()
        {
            WorldSummary Sample(string branch,string snapshot)=>new() {worldId=new string('a',32),historyVersion=1,
                branchId=branch,snapshot=snapshot,sequence=9,revision=long.MaxValue,ancestors=new List<WorldAncestor>()};
            return new[]{Sample(new string('0',32),new string('0',64)),
                Sample(new string('b',31)+"1",new string('c',63)+"1"),
                Sample(new string('b',31)+"2",new string('c',63)+"2"),
                Sample(new string('b',31)+"1",new string('c',63)+"3"),
                Sample(new string('b',31)+"1",new string('c',63)+"1")};
        }
        private static void RenderAt(int width,int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера лобби").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Лобби").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var view=WorldLobbyView.Create(canvas.transform);
            var samples=IdentitySamples();var branches=samples.Select(copy=>copy.branchId).ToArray();var snapshots=samples.Select(copy=>copy.snapshot).ToArray();
            for(int i=0;i<view.Choices.Length;i++)
            {
                var copy=samples[i%samples.Length];
                view.ShowChoice(i,i==0?new string('Ш',48):"Совместный завод",new string('Ш',48),copy.revision,
                    WorldLobbyView.IdentityLabel(copy.branchId,branches),WorldLobbyView.IdentityLabel(copy.snapshot,snapshots),
                    WorldLobbyView.RelationText(copy.CompareTo(samples[0]),i==0),i==0,true);
            }
            view.Status.text="Получение и проверка выбранного сохранения…";
            Canvas.ForceUpdateCanvases();
            foreach(var button in view.Choices)
            {
                var text=button.GetComponentInChildren<Text>();
                if(text.fontSize!=16 || text.supportRichText || text.resizeTextForBestFit)
                    throw new InvalidOperationException("Подписи лобби не обновлены: выполните WorldLobbyAuthoring.RefineIdentityLabels.");
                if(text.preferredHeight>text.rectTransform.rect.height+.1f) throw new InvalidOperationException($"Строка лобби обрезана: нужно {text.preferredHeight}, доступно {text.rectTransform.rect.height}.");
            }
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../..",$".reference/tests/world-lobby-{width}x{height}.png")),image.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
