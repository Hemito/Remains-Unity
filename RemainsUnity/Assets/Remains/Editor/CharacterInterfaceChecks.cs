using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
namespace Remains.Editor
{
    public static class CharacterInterfaceChecks
    {
        public static void Run()
        {
            WeaponCatalog.Load();PonyAppearance.Prepare();
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/character-contract-"+Guid.NewGuid().ToString("N")));
            var library=new CharacterLibrary(root);
            var actor=new ActorState {id="player:Литлпип",character="Литлпип",player=true,appearance=new CharacterAppearance {version=1,hairStyle=5,eyeStyle=6,secondaryHairEnabled=true},level="hub"};
            PlayerStarterInventory.Give(actor);actor.weaponContent=WeaponCatalog.ManifestCopy();
            var entry=library.Create(actor);string source=File.ReadAllText(Path.Combine(entry.directory,"character.json"));
            Reject(()=>library.Create(actor),"Повторное создание");var duplicate=actor.Copy();duplicate.character="литлпип";duplicate.id="player:"+duplicate.character;
            Reject(()=>library.Create(duplicate),"Регистр имени");
            if(File.ReadAllText(Path.Combine(entry.directory,"character.json"))!=source) throw new InvalidOperationException("Повторное создание заменило персонажа.");
            foreach(string invalid in new[]{"", " "," имя","имя ","..","A/B","CON","lPt1",new string('А',33)}) Reject(()=>CharacterName.ValidateNew(invalid),"Недопустимое имя "+invalid);
            File.WriteAllText(Path.Combine(entry.directory,"world.json"),"Недоступное для парсинга содержимое мира");
            Directory.CreateDirectory(Path.Combine(root,"Ошибочный"));File.WriteAllText(Path.Combine(root,"Ошибочный","character.json"),"{}");
            var list=library.Read();
            if(list.Count!=2 || list.Find(value=>value.name==actor.character)?.actor?.appearance?.hairStyle!=5 || list.Find(value=>value.name=="Ошибочный")?.error==null)
                throw new InvalidOperationException("Список персонажей потерял профиль, внешность или ошибку.");
            var saved=new SaveStore(entry.directory).LoadCharacter();
            if(saved.weaponInventoryVersion!=1 || saved.inventory.Find(item=>item.id==saved.equippedWeaponItem)?.definition!="pistol")
                throw new InvalidOperationException("Новый персонаж требует миграции или не имеет экипированного оружия.");
            double clock=10;var time=new CharacterPlayTime(()=>clock);time.Checkpoint(entry.directory);clock=70;time.Checkpoint(entry.directory);time.Stop();
            clock=1000;time.Checkpoint(entry.directory);clock=1060;time.Checkpoint(entry.directory);time.Stop();
            var record=CharacterTimeRecord.Read(entry.directory);
            if(record.seconds!=120 || !record.completeHistory) throw new InvalidOperationException("Время повторено, сброшено или считает отключённую сессию.");
            string old=Path.Combine(root,"Старый");Directory.CreateDirectory(old);clock=2000;time.Checkpoint(old);clock=2010;time.Checkpoint(old);time.Stop();
            if(CharacterTimeRecord.Read(old).completeHistory || CharacterTimeRecord.Read(old).seconds!=10)
                throw new InvalidOperationException("Выдумано историческое время старого профиля.");
            var parent=new GameObject("Проверка редактора",typeof(RectTransform));
            try
            {
                var view=CharacterEditorView.CreateView(parent.transform);view.Validate();
                var panel=view.gameObject.AddComponent<CharacterEditorPanel>();panel.Initialize(view,library,new InventoryArtwork(),_=>{},()=>{});
                view.HairPrevious.onClick.Invoke();view.EyePrevious.onClick.Invoke();view.SecondaryEnabled.isOn=true;
                view.Colors[2].onClick.Invoke();view.Hex.text="BEEF12";view.Hex.onEndEdit.Invoke(view.Hex.text);
                if(panel.Preview.appearance.hairStyle!=5 || panel.Preview.appearance.eyeStyle!=6 || panel.Preview.appearance.secondaryHair!=0xBEEF12)
                    throw new InvalidOperationException("Настройки редактора не применились к превью.");
                view.SecondaryEnabled.isOn=false;
                if(view.Colors[2].interactable || view.Hex.interactable || Array.Exists(view.Channels,channel=>channel.interactable))
                    throw new InvalidOperationException("Выключенный второй цвет оставил активные зависимые настройки.");
                view.Reset.onClick.Invoke();
                if(panel.Preview.appearance.hairStyle!=1 || panel.Preview.appearance.secondaryHairEnabled)
                    throw new InvalidOperationException("Сброс редактора не восстановил исходную внешность.");
                var cards=CharacterLibraryView.CreateView(parent.transform);cards.Validate();
                cards.Portraits[0].Show(actor,new InventoryArtwork());
                if(cards.Portraits[0].Weapon.sprite==null || !cards.Portraits[0].Weapon.enabled)
                    throw new InvalidOperationException("Карточка потеряла последнее оружие.");
                MainMenuPrefabChecks.Run();
            }
            finally {UnityEngine.Object.DestroyImmediate(parent);}
            Debug.Log("Редактор и персонажи: PASS — формы/цвета/зависимости, точное сохранение, занятое имя без замены, видимые ошибки, список без чтения миров, стартовое оснащение и время без учёта отключения.");
        }
        private static void Reject(Action action,string context)
        {
            try {action();} catch(Exception error) when(error is ArgumentException || error is InvalidOperationException || error is IOException) {return;}
            throw new InvalidOperationException("Не отклонено: "+context);
        }
        public static void Render()
        {
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1920,1080),new Vector2Int(2560,1080)}) RenderAt(size.x,size.y);
        }
        private static void RenderAt(int width,int height)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера персонажей").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.015f,.03f,.025f);
            var target=new RenderTexture(width,height,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("Просмотр персонажей").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var graphics=new InventoryArtwork();
            var editor=CharacterEditorView.CreateView(canvas.transform);
            var panel=editor.gameObject.AddComponent<CharacterEditorPanel>();panel.Initialize(editor,new CharacterLibrary(Path.Combine(Application.dataPath,"../../.reference/tests/character-render")),graphics,_=>{},()=>{});
            editor.Name.text="Литлпип";editor.HairPrevious.onClick.Invoke();editor.EyePrevious.onClick.Invoke();editor.SecondaryEnabled.isOn=true;
            editor.Colors[2].onClick.Invoke();editor.Swatches[6].onClick.Invoke();
            Capture(camera,target,"character-editor");editor.gameObject.SetActive(false);
            var cards=CharacterLibraryView.CreateView(canvas.transform);cards.Status.text="Выберите персонажа для своей игры или подключения к другу.";
            for(int i=0;i<6;i++)
            {
                var actor=new ActorState {character=i==0?new string('Ш',32):"Персонаж "+(i+1),player=true,appearance=new CharacterAppearance {version=1,hairStyle=i%5+1,eyeStyle=i+1,hair=CharacterEditorPanel.Palette[i+3],secondaryHairEnabled=i%2==0}};
                PlayerStarterInventory.Give(actor);cards.Portraits[i].Show(actor,graphics);cards.Names[i].text=actor.character;cards.Times[i].text=i==0?"Время ещё не учтено":"В игре: 12 ч 34 мин";
                if(cards.Names[i].preferredHeight>cards.Names[i].rectTransform.rect.height) throw new InvalidOperationException("Имя обрезано в карточке.");
            }
            Capture(camera,target,"character-library");camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);
            void Capture(Camera view,RenderTexture texture,string name)
            {
                Canvas.ForceUpdateCanvases();view.Render();RenderTexture.active=texture;
                var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
                try {image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/"+name+"-"+width+"x"+height+".png")),image.EncodeToPNG());}
                finally {RenderTexture.active=null;UnityEngine.Object.DestroyImmediate(image);}
            }
        }
    }
}
