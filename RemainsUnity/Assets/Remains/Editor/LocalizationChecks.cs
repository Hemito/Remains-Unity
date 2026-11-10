using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading.Tasks;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;

namespace Remains.Editor
{
    public static class LocalizationChecks
    {
        public static async void RunBatch()
        {try{Run();await RenderSnapshots();EditorApplication.Exit(0);}catch(Exception failure){Debug.LogException(failure);EditorApplication.Exit(1);}}
        public static void BuildBatch()
        {try{Run();PrototypeBuild.BuildTo("Builds/Localization/Remains.exe");EditorApplication.Exit(0);}catch(Exception failure){Debug.LogException(failure);EditorApplication.Exit(1);}}
        [MenuItem("Remains/Локализация/Проверить пакет")]
        public static void Run()
        {
            var pack=GameLocalization.Parse(File.ReadAllText(LocalizationAuthoring.AssetPath));
            Require(pack.languages.Length==9,"Не импортированы все девять исходных языков.");
            var russian=pack.languages.Single(x=>x.code=="ru");
            Require(russian.sourceSha256=="acd503ab8ec20428c2d625865d10684a26a3c20e0a056ea2ee2be46d9fd9a6ad","Изменился проверенный русский источник.");
            foreach(var metadata in pack.languages)
            {
                var language=GameLocalization.LoadLanguage(metadata.code);
                Require(language.entries.Length>2000 && language.items.Length>1000,"Неполный языковой пакет: "+language.code);
                var xml=XElement.Parse(language.sourceXml);
                Require(xml.Elements().Count()>2000,"Потеряны исходные узлы: "+language.code);
                Require(language.entries.Any(x=>x.key.Contains("pip:hp[1]")),"Потерян повторный исходный ключ hp: "+language.code);
                Require(language.items.Any(x=>x.id=="weapon:spear:grade:0"),"Нет связи стабильного ID оружия с переводом: "+language.code);
            }
            var duplicate=LocalizationAuthoring.Flatten(XElement.Parse("<all><pip id='hp'><n>Здоровье</n></pip><pip id='hp'><n>ОЗ</n></pip><txt id='test' s1='up'><n>@1 #1 [br] [текст]</n></txt></all>"));
            Require(duplicate.Count==3 && duplicate["txt:test[0]/n[0]"]=="@1 #1 [br] [текст]","Повторные ключи или маркеры исходного текста изменены.");
            var source=ItemTextCatalog.Load();
            GameLocalization.Initialize();string original=GameLocalization.CurrentLanguage;
            bool hadPreference=PlayerPrefs.HasKey(GameLocalization.Preference);string preference=PlayerPrefs.GetString(GameLocalization.Preference,"");
            var node=new GameObject("Localization check",typeof(RectTransform),typeof(Text));var label=node.GetComponent<Text>();
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.text="Фонарик";LocalizedUiText.Bind(label);
            var inputNode=new GameObject("User input",typeof(RectTransform),typeof(InputField));inputNode.transform.SetParent(node.transform,false);
            var inputTextNode=new GameObject("User text",typeof(RectTransform),typeof(Text));inputTextNode.transform.SetParent(inputNode.transform,false);
            var userText=inputTextNode.GetComponent<Text>();userText.font=label.font;
            var input=inputNode.GetComponent<InputField>();input.textComponent=userText;input.text="Фонарик";LocalizedUiText.Bind(userText);
            try
            {
                GameLocalization.Select("en");
                Require(source.Name("weapon:spear:grade:0","localization-check")!="Копьё","Загруженный каталог предметов не сменил язык.");
                Require(GameLocalization.TranslateRussian("Фонарик","localization-check")=="Flashlight","Не подключён собственный текст фонарика.");
                Require(label.text=="Flashlight","Видимая подпись не сменила язык по событию.");
                Require(PlayerPrefs.GetString(GameLocalization.Preference)=="en","Выбранный язык не сохранён локально.");
                Require(input.text=="Фонарик" && userText.text=="Фонарик" && userText.GetComponent<LocalizedUiText>()==null,"Локализация изменила пользовательский ввод.");
                label.text="Закрыть";Require(label.text=="Close","Новое значение подписи не переведено по событию Text.");
                foreach(var language in pack.languages)
                {
                    GameLocalization.Select(language.code);
                    Require(!string.IsNullOrWhiteSpace(label.text),"Пустая подпись языка: "+language.code);
                    var font=LocalizationFonts.ForLanguage(language.code);
                    string sample=language.code=="zh"?"语言":language.code=="tw"?"語言":language.code=="jp"?"言語":"Язык";
                    font.RequestCharactersInTexture(sample,18);
                    Require(sample.All(font.HasCharacter),"Шрифт не содержит знаки языка: "+language.code);
                }
                GameLocalization.Select("ru");Require(source.Name("weapon:spear:grade:0","localization-check")=="Копьё","Русский исходный текст не восстановлен.");
                Require(label.text=="Закрыть","Восстановлен перевод вместо исходной русской подписи.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(node);GameLocalization.Select(original);
                if(hadPreference)PlayerPrefs.SetString(GameLocalization.Preference,preference);else PlayerPrefs.DeleteKey(GameLocalization.Preference);
                PlayerPrefs.Save();
            }
            Debug.Log("Локализация: PASS — 9 языков, происхождение ru, повторные записи, исходные маркеры, стабильные ID и смена языка в готовом каталоге.");
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Локализация: "+message);}
        public static async Task RenderSnapshots()
        {
            string original=GameLocalization.CurrentLanguage;
            bool hadPreference=PlayerPrefs.HasKey(GameLocalization.Preference);string preference=PlayerPrefs.GetString(GameLocalization.Preference,"");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера проверки локализации").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Проверка локализации").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var picker=new LanguageSettingsPanel(canvas.transform,ui);
            var menu=new MainMenuPanel(canvas.transform,()=>{},()=>{},()=>{},()=>{},()=>{},controls:()=>{},language:picker.Open);
            var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/localization"));Directory.CreateDirectory(output);
            var diagnostics=new JArray();
            try
            {
                foreach(string code in new[]{"en","zh","ru"})
                {
                    picker.Open();picker.ChoiceRect(code).GetComponent<Button>().onClick.Invoke();
                    Require(GameLocalization.CurrentLanguage==code,"Кнопка языка не выбрала "+code);
                    LocalizationFonts.ApplyTree(canvas.transform);picker.RefreshFonts();
                    picker.Close();await Capture("menu-"+code);picker.Open();await Capture("languages-"+code);
                }
                File.WriteAllText(Path.Combine(output,"font-diagnostics.json"),diagnostics.ToString());
            }
            finally
            {
                picker.Dispose();UnityEngine.Object.DestroyImmediate(canvas.gameObject);GameLocalization.Select(original);
                if(hadPreference)PlayerPrefs.SetString(GameLocalization.Preference,preference);else PlayerPrefs.DeleteKey(GameLocalization.Preference);PlayerPrefs.Save();
                RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
            async Task Capture(string name)
            {
                foreach(var text in canvas.GetComponentsInChildren<Text>(false))text.font.RequestCharactersInTexture(text.text,text.fontSize,text.fontStyle);
                foreach(var text in canvas.GetComponentsInChildren<Text>(false))text.SetAllDirty();
                // Runtime-created OS font textures upload on the next Editor frame, not inside Camera.Render.
                await Task.Delay(150);
                foreach(var text in canvas.GetComponentsInChildren<Text>(false))text.SetAllDirty();
                Canvas.ForceUpdateCanvases();await Task.Delay(50);
                foreach(var text in canvas.GetComponentsInChildren<Text>(false).Where(x=>!string.IsNullOrWhiteSpace(x.text)))
                {
                    var texture=text.font.material?.mainTexture;
                    diagnostics.Add(new JObject{["frame"]=name,["text"]=text.text,["font"]=text.font.name,["fontNames"]=new JArray(text.font.fontNames),["shader"]=text.material.shader.name,["texture"]=texture?.name,["size"]=texture==null?"null":texture.width+"x"+texture.height,["preferredWidth"]=text.preferredWidth,["vertexCount"]=text.cachedTextGenerator.vertexCount});
                }
                Canvas.ForceUpdateCanvases();Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
            }
        }
    }
}
