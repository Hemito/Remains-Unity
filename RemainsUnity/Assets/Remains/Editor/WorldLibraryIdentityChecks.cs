using System;
using System.IO;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.UI;
using Remains.Content;

namespace Remains.Editor
{
    public static class WorldLibraryIdentityChecks
    {
        public static readonly string LibraryWarning="В библиотеке есть предупреждения; подробности — в списке миров и журнале.";
        public static void CheckLabels()
        {
            string prefix="0123456789ab";
            string a=prefix+"a"+new string('0',19),b=prefix+"b"+new string('0',19);
            string sa=prefix+"a"+new string('1',51),sb=prefix+"b"+new string('1',51);
            // The colliding sibling is on the next page (five rows per page).
            var branches=new[]{a,"other-branch-1","other-branch-2","other-branch-3","other-branch-4",b,a};
            var snapshots=new[]{sa,"other-snapshot-1","other-snapshot-2","other-snapshot-3","other-snapshot-4",sb,sa};
            var labels=new WorldCopyLabels(branches,snapshots);
            Require(labels.Branch(a)==prefix+"a" && labels.Branch(b)==prefix+"b","Коллизия ветвей разных страниц не различается");
            Require(labels.Snapshot(sa)==prefix+"a" && labels.Snapshot(sb)==prefix+"b","Коллизия снимков разных страниц не различается");
            string first=WorldCopyLabels.ChoiceText("Завод",labels.Branch(a),labels.Snapshot(sa),7,"Другая ветвь",false);
            string sibling=WorldCopyLabels.ChoiceText("Завод",labels.Branch(b),labels.Snapshot(sa),7,"Другая ветвь",false);
            string sameBranchA=WorldCopyLabels.ChoiceText("Завод",labels.Branch(a),labels.Snapshot(sa),7,"Связь копий не подтверждена",false);
            string sameBranchB=WorldCopyLabels.ChoiceText("Завод",labels.Branch(a),labels.Snapshot(sb),7,"Связь копий не подтверждена",false);
            Require(first!=sibling && sameBranchA!=sameBranchB && first.Contains("Состояние №7"),"Равное состояние скрывает независимые копии");
            string endA=new string('a',31)+"0",endB=new string('a',31)+"1",endSnapshotA=new string('f',63)+"0",endSnapshotB=new string('f',63)+"1";
            var lastDigit=new WorldCopyLabels(new[]{endA,endB},new[]{endSnapshotA,endSnapshotB});
            Require(lastDigit.Branch(endA)==endA && lastDigit.Branch(endB)==endB && lastDigit.Snapshot(endSnapshotA)==endSnapshotA && lastDigit.Snapshot(endSnapshotB)==endSnapshotB,
                "Различие последнего символа полного ID обрезано");
            var duplicates=new WorldCopyLabels(new[]{a,a},new[]{sa,sa});
            Require(duplicates.Branch(a)==prefix && duplicates.Snapshot(sa)==prefix,"Повтор той же ветви/снимка удлиняет подпись");
            string selected=WorldCopyLabels.ChoiceText("Завод",labels.Branch(a),labels.Snapshot(sa),7,"не должно быть видно",true);
            Require(selected.Contains("Выбрана для запуска") && !selected.Contains("не должно быть видно") && selected!=first,
                "Выделение выбранного снимка потеряно или выводится старое отношение");
            Require(labels.Branch(null)=="нет данных" && labels.Snapshot("")=="нет данных","Для отсутствующей истории выдумана идентичность");
            bool refused=false;try {labels.Snapshot("not-captured");} catch(InvalidOperationException) {refused=true;}
            Require(refused,"Новый снимок вне прочитанного набора молча получил подпись");
            string menuA=MainMenuPanel.DescribeWorld("Завод",a,7,sa),menuB=MainMenuPanel.DescribeWorld("Завод",a,7,sb);
            Require(menuA!=menuB && menuA.Contains(sa) && menuB.Contains(sb),"Главное меню скрывает разные снимки при общих префиксах");
            Require(menuA.Contains("Ветвь "+prefix) && menuA.Contains("состояние №7"),"Главное меню потеряло ветвь/состояние");
            Require(MainMenuPanel.DescribeWorld("Завод",null,7,null).Contains("нет данных"),"Старые данные не обозначены явно");
            Console.WriteLine("Подписи библиотеки: PASS — все страницы, коллизии ветвей/снимков, одинаковый номер, общие ID, выбранная копия, полная идентичность меню.");
        }
        public static void Run() {CheckLabels();CheckAndRender(false);}
        public static void Render() {CheckLabels();CheckAndRender(true);}
        private static void CheckAndRender(bool render)
        {
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                var root=new GameObject("Подписи локальных копий",typeof(RectTransform));Camera camera=null;RenderTexture target=null;Texture2D image=null;
                var previous=RenderTexture.active;
                try
                {
                    var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
                    // A ScreenSpaceCamera Canvas owns its world transform. Parenting
                    // its camera below it creates a cyclic placement dependency.
                    camera=new GameObject("Камера подписей копий").AddComponent<Camera>();
                    camera.transform.position=new Vector3(0,0,-10);canvas.worldCamera=camera;canvas.planeDistance=1;
                    target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                    var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                    var menu=new MainMenuPanel(root.transform,()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{});
                    var sourceMenu=GameAssets.Load<MainMenuView>("Presentation/MainMenu");
                    menu.World.text=MainMenuPanel.DescribeWorld(new string('Ш',48),new string('a',32),long.MaxValue,new string('f',64))+"\n"+LibraryWarning;
                    Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
                    CheckCanvas(canvas,camera,size);
                    CheckText(menu.World,size);CheckCorners(menu.Controls.GetComponent<RectTransform>(),camera,size);
                    var picker=WorldPickerView.Create(root.transform);picker.Empty.gameObject.SetActive(false);picker.Warning.gameObject.SetActive(false);
                    var original=GameAssets.Load<WorldPickerView>("Presentation/WorldPicker");
                    for(int i=0;i<picker.Choices.Length;i++)
                    {
                        picker.ShowChoice(i,new string('Ш',48),new string('a',31)+i,new string('f',63)+i,long.MaxValue,
                            "Другая ветвь — прогресс не объединяется",i==1);
                        Require(picker.Choices[i].GetComponentInChildren<Text>().fontSize==16,"Выполните WorldLibraryIdentityAuthoring.Run: старая подпись списка");
                    }
                    Canvas.ForceUpdateCanvases();camera.Render();Canvas.ForceUpdateCanvases();
                    foreach(var button in picker.Choices) {CheckText(button.GetComponentInChildren<Text>(),size);CheckCorners(button.GetComponent<RectTransform>(),camera,size);}
                    foreach(var button in new[]{picker.Previous,picker.Next,picker.Close}) CheckCorners(button.GetComponent<RectTransform>(),camera,size);
                    for(int i=1;i<picker.Choices.Length;i++)
                    {
                        var before=picker.Choices[i-1].GetComponent<RectTransform>();var after=picker.Choices[i].GetComponent<RectTransform>();
                        Require(before.anchoredPosition.y-before.rect.height>=after.anchoredPosition.y,"Строки библиотеки перекрываются");
                    }
                    var last=picker.Choices[picker.Choices.Length-1].GetComponent<RectTransform>();
                    Require(last.anchoredPosition.y-last.rect.height>=picker.Close.GetComponent<RectTransform>().anchoredPosition.y,"Строка закрывает навигацию библиотеки");
                    Require(sourceMenu.World.text!=menu.World.text && original.Choices[0].GetComponentInChildren<Text>().text!=picker.Choices[0].GetComponentInChildren<Text>().text,
                        "Рабочие подписи изменили исходные префабы");
                    if(render)
                    {
                        image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                        void Save(string kind)
                        {
                            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                            image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests",$"world-library-identity-{kind}-{size.x}x{size.y}.png"));
                            File.WriteAllBytes(path,image.EncodeToPNG());
                        }
                        Save("picker");picker.gameObject.SetActive(false);Save("menu");
                    }
                }
                finally
                {
                    RenderTexture.active=previous;if(image!=null) UnityEngine.Object.DestroyImmediate(image);
                    if(target!=null) {camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
                    UnityEngine.Object.DestroyImmediate(root);if(camera!=null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
                }
            }
            Debug.Log("Подписи библиотеки: PASS — сохранённые поля меню/списка, длинные ID/имя, поля кадра и навигация при трёх разрешениях.");
        }
        private static void CheckText(Text text,Vector2Int size)
        {
            Require(!text.supportRichText,$"Подпись мира {text.name} интерпретирует имя как rich text");
            Require(text.preferredHeight<=text.rectTransform.rect.height+.1f,$"Подпись мира обрезается {size}: {text.name}, требуется={text.preferredHeight}, поле={text.rectTransform.rect.height}");
        }
        private static void CheckCorners(RectTransform rect,Camera camera,Vector2Int size)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            foreach(var corner in corners)
            {var point=camera.WorldToViewportPoint(corner);Require(point.x>=0 && point.y>=0 && point.x<=1 && point.y<=1,$"Подпись мира вне кадра {size}: {rect.name}, point={point}");}
        }
        private static void CheckCanvas(Canvas canvas,Camera camera,Vector2Int size)
        {
            Require(canvas.renderMode==RenderMode.ScreenSpaceCamera && canvas.worldCamera==camera &&
                !camera.transform.IsChildOf(canvas.transform) && camera.targetTexture.width==size.x && camera.targetTexture.height==size.y,
                "Камера native-проверки должна быть независима от своего Canvas и иметь целевой viewport");
            var canvasRect=canvas.GetComponent<RectTransform>();
            var center=camera.WorldToViewportPoint(canvasRect.TransformPoint(canvasRect.rect.center));
            Require(Mathf.Abs(center.x-.5f)<.001f && Mathf.Abs(center.y-.5f)<.001f && Mathf.Abs(center.z-canvas.planeDistance)<.001f,
                $"Canvas native-проверки не подготовлен камерой {size}: center={center}, plane={canvas.planeDistance}");
        }
        private static void Require(bool condition,string reason) {if(!condition) throw new InvalidOperationException("Библиотека копий: "+reason);}
    }
}
