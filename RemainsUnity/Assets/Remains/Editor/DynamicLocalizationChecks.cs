using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    public static class DynamicLocalizationChecks
    {
        public static async void RunBatch()
        {try{UnityLocalizationAuthoring.Migrate();await Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static async Task Run()
        {
            GameLocalization.Initialize();string original=GameLocalization.CurrentLanguage;
            bool had=PlayerPrefs.HasKey(GameLocalization.Preference);string preference=PlayerPrefs.GetString(GameLocalization.Preference,"");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера динамической локализации").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Динамическая локализация",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var description=ui.Label(canvas.transform,"",new Vector2(30,-30),new Vector2(1200,230));
            var button=ui.Button(canvas.transform,"",new Vector2(30,-280),new Vector2(650,40),()=>{});
            var panel=canvas.gameObject.AddComponent<SelectedWorldPanel>();panel.Initialize(description,button);
            const string worldName="Мир faecbc23";
            Set(panel,"cachedSummary",new WorldSummary{branchId="f8f449b1a3f34aa98b252d1500070001",revision=2835,snapshot=new string('a',64)});
            Set(panel,"cachedName",worldName);Set(panel,"cachedWarnings",false);Set(panel,"hasResult",true);
            var hint=new InteractionHint(canvas.transform,ui);
            var point=new TravelPoint{id="locale-map",title="Карта Пустоши",x=0,y=0,width=1,height=1};
            var hintLabel=(Text)Get(hint,"label");
            var profiles=FactoryProfileCatalog.Load();
            var factory=ui.Label(canvas.transform,"",new Vector2(30,-360),new Vector2(1200,210));
            string priorWorld=null,priorHint=null;
            var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/unity-localization/dynamic"));Directory.CreateDirectory(output);
            try
            {
                foreach(string code in new[]{"en","ru","ja"})
                {
                    GameLocalization.Select(code);panel.RefreshLanguage();hint.InvalidateLanguage();hint.Refresh(point,false,null);
                    Require(description.text.Contains(worldName),"Изменено пользовательское имя мира: "+code);
                    Require(priorWorld==null || priorWorld!=description.text,"Описание мира осталось на предыдущем языке: "+code);
                    Require(priorHint==null || priorHint!=hintLabel.text,"Подсказка карты осталась на предыдущем языке: "+code);
                    Require(code=="ru" || !hintLabel.text.Contains(point.title),"Название точки перехода не переведено: "+code);
                    priorWorld=description.text;priorHint=hintLabel.text;
                    var profile=profiles.Main;
                    factory.text=GameLocalization.UiText("factory.profile."+profile.id+".title",profile.title,"dynamic-check")+"\n"+GameLocalization.UiText("factory.profile."+profile.id+".description",profile.description,"dynamic-check");
                    Require(code=="ru" || !factory.text.Contains(profile.title),"Вид завода не переведён: "+code);
                    LocalizationFonts.ApplyTree(canvas.transform);
                    foreach(var text in canvas.GetComponentsInChildren<Text>(true)){text.font.RequestCharactersInTexture(text.text,text.fontSize,text.fontStyle);text.SetAllDirty();}
                    await Task.Delay(150);Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                    File.WriteAllBytes(Path.Combine(output,code+".png"),image.EncodeToPNG());
                    File.WriteAllText(Path.Combine(output,code+".txt"),description.text+"\n\n"+hintLabel.text+"\n\n"+factory.text);
                }
                Debug.Log("DynamicLocalizationChecks PASS: существующий выбранный мир и подсказка карты en→ru→ja, имя пользователя сохранено, профиль завода переведён; кадры .reference/unity-localization/dynamic.");
            }
            finally
            {
                hint.Dispose();UnityEngine.Object.DestroyImmediate(canvas.gameObject);GameLocalization.Select(original);
                if(had)PlayerPrefs.SetString(GameLocalization.Preference,preference);else PlayerPrefs.DeleteKey(GameLocalization.Preference);PlayerPrefs.Save();
                RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static object Get(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException("Динамическая локализация: "+reason);}
    }
}
