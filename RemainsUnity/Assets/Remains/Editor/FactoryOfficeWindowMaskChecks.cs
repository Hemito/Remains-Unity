using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryOfficeWindowMaskChecks
    {
        [Serializable] private sealed class RectEvidence
        {public float x,y,width,height;public RectEvidence(Rect value) {x=value.x;y=value.y;width=value.width;height=value.height;}}
        [Serializable] private sealed class WindowEvidence {public string sourceRecord;public RectEvidence bounds;}
        [Serializable] private sealed class RenderEvidence
        {public int schema=1,changedPixels,outsideWindowPixels;public int[] pixels;public RectEvidence camera;public WindowEvidence[] windows;public string evidence;}
        public static void RunAndRender() {Run();RenderScreenshots();}
        public static void Run()
        {
            FactoryOfficeWindowMasks.ValidateResources();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryOfficeDraft.Path);
            var mask=source.GetComponent<RoomWindowMaskAuthoring>()??throw new InvalidOperationException("Офис: сохранённые маски окон ещё не установлены.");
            mask.Validate();FactoryOfficeWindowMasks.VerifyPixels(mask);
            bool composition=source.GetComponent<RoomBackdropCompositionAuthoring>()!=null;
            Require(mask.Regions.Length==(composition?8:7) && mask.Layers.Length==(composition?39:38) && mask.Visibility.width==1920 && mask.Visibility.height==1000,"Сохранены семь окон и явное дополнение chole/нативный atlas");
            var room=source.GetComponent<RoomAuthoring>();
            Require(mask.Layers.Count(l=>l.GetComponent<RoomArtworkAuthoring>().Read(room).order==-29)==31,"Только31 фоновая геометрия и7 окон имеют маску");
            Require(source.GetComponentsInChildren<RoomArtworkAuthoring>().Where(a=>!mask.Layers.Contains(a.GetComponent<MeshRenderer>())).All(a=>{var material=a.GetComponent<MeshRenderer>().sharedMaterial;return !material.HasProperty("_ArtworkVisibilityEnabled") || material.GetFloat("_ArtworkVisibilityEnabled")==0;}),"Фронт/общий задник/прочий декор не вырезаются");
            var clone=UnityEngine.Object.Instantiate(source);
            try
            {
                var copy=clone.GetComponent<RoomWindowMaskAuthoring>();clone.transform.position=new Vector3(76.8f,0,0);copy.Validate();
                var window=copy.Regions[0].artwork.transform;window.localPosition+=Vector3.right*.1f;
                Reject(copy.Validate,"Перемещение окна выявляет устаревший atlas");window.localPosition-=Vector3.right*.1f;
                var background=copy.Layers.First(l=>l.sortingOrder==-29).transform;background.localPosition+=Vector3.up*.1f;
                Reject(copy.Validate,"Перемещение поверхности выявляет устаревший второй UV");background.localPosition-=Vector3.up*.1f;copy.Validate();
            }
            finally {UnityEngine.Object.DestroyImmediate(clone);}
            Debug.Log("Маски окон: PASS, exact resource/importer, saved atlas/"+mask.Layers.Length+" bindings, семь окон сохранены отдельно от chole, nested translation, stale window/geometry negatives.");
        }
        public static void RenderScreenshots()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Кадры масок требуют графического устройства: используйте batchmode без -nographics. Проверки авторских данных доступны без GPU.");
            RoomAuthoringScenes.ValidateSavedOverrides();EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FactoryOfficeDraft.Path));
            var room=root.GetComponent<RoomAuthoring>();var mask=root.GetComponent<RoomWindowMaskAuthoring>();
            mask.Validate();RoomLightingPreview.Refresh(room);
            var camera=new GameObject("Предпросмотр масок офиса").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=11;
            camera.transform.position=new Vector3(19.2f,10,-10);camera.backgroundColor=new Color(.08f,.09f,.1f);
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var pixelImage=new Texture2D(1920,1080,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            var originals=mask.Layers.Select(l=>l.sharedMaterial).ToArray();var copies=originals.Select(m=>new Material(m)).ToArray();
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/office-window-mask-r31"));Directory.CreateDirectory(folder);
            Color32[] Capture(string name)
            {
                camera.Render();RenderTexture.active=target;pixelImage.ReadPixels(new Rect(0,0,1920,1080),0,0);pixelImage.Apply();
                File.WriteAllBytes(Path.Combine(folder,name+".png"),pixelImage.EncodeToPNG());return pixelImage.GetPixels32();
            }
            try
            {
                for(int i=0;i<mask.Layers.Length;i++) {copies[i].SetFloat("_ArtworkVisibilityEnabled",0);mask.Layers[i].sharedMaterial=copies[i];}
                var before=Capture("without-cutouts");
                for(int i=0;i<mask.Layers.Length;i++) mask.Layers[i].sharedMaterial=originals[i];
                var after=Capture("with-cutouts");int changed=0,outside=0;
                float width=camera.orthographicSize*2*camera.aspect;var envelope=new Rect(19.2f-width/2,-1,width,22);
                for(int y=0;y<1080;y++) for(int x=0;x<1920;x++)
                {
                    if(before[y*1920+x].Equals(after[y*1920+x])) continue;changed++;
                    var world=new Vector2(envelope.x+(x+.5f)*envelope.width/1920,envelope.y+(y+.5f)*envelope.height/1080);
                    if(!mask.Regions.Any(r=>new Rect(r.bakedBounds.x-.06f,r.bakedBounds.y-.06f,r.bakedBounds.width+.12f,r.bakedBounds.height+.12f).Contains(world))) outside++;
                }
                File.WriteAllText(Path.Combine(folder,"render-report.json"),StateJson.ToJson(new RenderEvidence {pixels=new[]{1920,1080},changedPixels=changed,outsideWindowPixels=outside,camera=new RectEvidence(envelope),
                    windows=mask.Regions.Select(r=>new WindowEvidence {sourceRecord=r.sourceRecord,bounds=new RectEvidence(r.bakedBounds)}).ToArray(),evidence="native Unity comparison; original runtime unverified"},true));
                Require(changed>1000 && outside==0,"GPU-композиция меняет только сохранённые области семи окон/явного chole");
                Debug.Log("Маски окон: actual Unity1920×1080 кадры сохранены; changedPixels="+changed+", outsideWindowPixels="+outside+". Это проверка новой композиции, не pixel-equivalence оригиналу.");
            }
            finally
            {
                for(int i=0;i<mask.Layers.Length;i++) mask.Layers[i].sharedMaterial=originals[i];
                foreach(var m in copies) UnityEngine.Object.DestroyImmediate(m);
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(pixelImage);UnityEngine.Object.DestroyImmediate(target);RoomLightingPreview.Clear();
            }
        }
        private static void Reject(Action action,string message)
        {
            bool rejected=false;try {action();} catch(InvalidOperationException) {rejected=true;}Require(rejected,message);
        }
        private static void Require(bool condition,string message)
        {if(!condition) throw new InvalidOperationException("Маска окон: "+message);}
    }
}
