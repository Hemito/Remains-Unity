using System.IO;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Remains.Content;

namespace Remains.Editor
{
    public static class CombatHudRender
    {
        public static void Render()=>Render(false);
        public static void RenderHealth()=>Render(true);
        public static void RenderReferenceSizes()
            =>RenderReferenceSizes(new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)},null);
        public static void RenderVectorReserve()
        {
            HudVectorReserveAuthoring.Check();
            string folder=VectorArtworkReserveManifest.RepositoryPath(".reference/tests/vector-artwork-r59/render");
            Directory.CreateDirectory(folder);
            RenderReferenceSizes(new[]{new Vector2Int(1920,1080),new Vector2Int(3840,2160)},folder);
        }
        private static void RenderReferenceSizes(Vector2Int[] sizes,string reserveFolder)
        {
            Remains.Content.WeaponCatalog.Load();
            foreach(var size in sizes)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Камера HUD").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
                var target=new RenderTexture(size.x,size.y,24);target.Create();camera.targetTexture=target;
                var canvas=new GameObject("HUD").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
                canvas.worldCamera=camera;canvas.planeDistance=1;
                var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                var hud=new CombatHud(canvas.transform,_=>{});
                var actor=new ActorState {id="hud-reference-render",character="Литлпип",weapon="pistol",health=100,mana=100};
                if(reserveFolder==null) WeaponAmmunition.Initialize(actor,true);
                else PlayerStarterInventory.Give(actor);
                var image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);
                var previous=RenderTexture.active;
                try
                {
                    for(int sample=0;sample<3;sample++)
                    {
                        actor.health=new[]{100f,19.1f,0f}[sample];actor.mana=new[]{100f,36f,0f}[sample];actor.down=sample==2;
                        hud.Refresh(actor,true);Canvas.ForceUpdateCanvases();
                        var view=canvas.GetComponentInChildren<CombatHudView>();
                        foreach(var rect in hud.LayoutReservations)
                        {
                            var corners=new Vector3[4];rect.GetWorldCorners(corners);
                            foreach(var corner in corners)
                            {
                                var viewport=camera.WorldToViewportPoint(corner);
                                if(viewport.x<0 || viewport.y<0 || viewport.x>1 || viewport.y>1)
                                    throw new System.InvalidOperationException($"HUD выходит за кадр {size}: {rect.name}, viewport={viewport}.");
                            }
                        }
                        if(view.HealthText.preferredHeight>view.HealthText.rectTransform.rect.height ||
                            view.HealthText.preferredWidth>view.HealthText.rectTransform.rect.width)
                            throw new System.InvalidOperationException($"Числа здоровья не помещаются в поле HUD {size}.");
                        if(reserveFolder==null) camera.Render();else RenderReserveCamera(camera,target);
                        RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();
                        string path=reserveFolder==null?
                            Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests",$"hud-reference-{size.x}x{size.y}-{sample}.png")):
                            Path.Combine(reserveFolder,$"hud-{size.x}x{size.y}-{sample}.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path,image.EncodeToPNG());
                        if(reserveFolder!=null) WriteReserveEvidence(path,size,sample,actor,view,camera,canvas);
                        Debug.Log($"HUD reference: {size}, HP={actor.health}, mana={actor.mana}, числа={view.HealthText.text}, кадр={path}.");
                    }
                }
                finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
            }
        }
        [System.Serializable] private sealed class ReserveHudFrame
        {
            public int schema=1,width,height,sample;
            public bool explicitDiagnosticFixture=true,originalRuntimeVerified=false;
            public float health,mana,healthFraction,manaFraction,canvasScale;
            public string healthText,pngSha256;
            public ReserveSpriteFrame healthSprite,manaSprite,frameSprite;
            public float[] healthScreenBounds,manaScreenBounds,frameScreenBounds;
        }
        [System.Serializable] public sealed class ReserveSpriteFrame
        {
            public string assetPath,guid,texturePath,textureSha256;
            public long localId;
            public float pixelsPerUnit;
            public float[] rect,pivotPixels,border,worldBounds;
            public int textureWidth,textureHeight;
        }
        public static ReserveSpriteFrame DescribeReserveSprite(Sprite sprite)
        {
            if(sprite==null || sprite.texture==null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out string guid,out long id))
                throw new System.InvalidOperationException("Рендер векторного резерва: нет действительного saved Sprite с GUID/localID.");
            var bounds=sprite.bounds;
            string texturePath=AssetDatabase.GetAssetPath(sprite.texture);
            return new ReserveSpriteFrame {assetPath=AssetDatabase.GetAssetPath(sprite),guid=guid,localId=id,texturePath=texturePath,
                textureSha256=VectorArtworkReserveManifest.Hash(VectorArtworkReserveManifest.RepositoryPath("RemainsUnity/"+texturePath)),
                pixelsPerUnit=sprite.pixelsPerUnit,textureWidth=sprite.texture.width,textureHeight=sprite.texture.height,
                rect=new[]{sprite.rect.x,sprite.rect.y,sprite.rect.width,sprite.rect.height},pivotPixels=new[]{sprite.pivot.x,sprite.pivot.y},
                border=new[]{sprite.border.x,sprite.border.y,sprite.border.z,sprite.border.w},
                worldBounds=new[]{bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y}};
        }
        private static void WriteReserveEvidence(string path,Vector2Int size,int sample,ActorState actor,CombatHudView view,Camera camera,Canvas canvas)
        {
            var saved=GameAssets.Load<CombatHudView>("Presentation/CombatHud");
            bool healthMatches=sample==1?System.Array.Exists(saved.HealthWarningFrames,s=>s==view.Health.sprite):view.Health.sprite==saved.Health.sprite;
            if(!healthMatches || view.Mana.sprite!=saved.Mana.sprite || Mathf.Abs(view.Health.fillAmount-actor.health/100)>1e-7f ||
                Mathf.Abs(view.Mana.fillAmount-actor.mana/100)>1e-7f)
                throw new System.InvalidOperationException("Рендер векторного HUD: фактические Sprite/заполнение не совпали с сохранёнными данными/HP.");
            var frame=view.Health.transform.parent.Find("Рамка здоровья")?.GetComponent<Image>();
            if(frame==null || frame.sprite==null || frame.type!=Image.Type.Sliced || frame.pixelsPerUnitMultiplier!=4)
                throw new System.InvalidOperationException("Рендер векторного HUD: отсутствует действительная сохранённая рамка здоровья.");
            var evidence=new ReserveHudFrame {width=size.x,height=size.y,sample=sample,health=actor.health,mana=actor.mana,
                healthText=view.HealthText.text,healthFraction=view.Health.fillAmount,manaFraction=view.Mana.fillAmount,canvasScale=canvas.scaleFactor,
                healthSprite=DescribeReserveSprite(view.Health.sprite),manaSprite=DescribeReserveSprite(view.Mana.sprite),frameSprite=DescribeReserveSprite(frame.sprite),
                healthScreenBounds=ScreenBounds(view.Health.rectTransform,camera),manaScreenBounds=ScreenBounds(view.Mana.rectTransform,camera),
                frameScreenBounds=ScreenBounds(frame.rectTransform,camera),pngSha256=VectorArtworkReserveManifest.Hash(path)};
            File.WriteAllText(Path.ChangeExtension(path,".json"),StateJson.ToJson(evidence,true));
        }
        private static float[] ScreenBounds(RectTransform rect,Camera camera)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            float minX=float.PositiveInfinity,minY=float.PositiveInfinity,maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
            foreach(var corner in corners)
            {
                var screen=camera.WorldToScreenPoint(corner);minX=Mathf.Min(minX,screen.x);minY=Mathf.Min(minY,screen.y);
                maxX=Mathf.Max(maxX,screen.x);maxY=Mathf.Max(maxY,screen.y);
            }
            return new[]{minX,minY,maxX,maxY};
        }
        // Both new visual proofs use the configured pipeline, never a substitute
        // built-in rasterizer when the project is running an SRP.
        internal static void RenderReserveCamera(Camera camera,RenderTexture texture)
        {
            SortingGroup.UpdateAllSortingGroups();
            if(GraphicsSettings.currentRenderPipeline==null) camera.Render();
            else
            {
                var request=new RenderPipeline.StandardRequest {destination=texture};
                if(!RenderPipeline.SupportsRenderRequest(camera,request))
                    throw new System.InvalidOperationException("Рендер векторного резерва: активный SRP не поддерживает StandardRequest.");
                RenderPipeline.SubmitRenderRequest(camera,request);
            }
        }
        private static void Render(bool healthSamples)
        {
            Remains.Content.WeaponCatalog.Load();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Камера").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
            var target=new RenderTexture(1440,320,24);target.Create();camera.targetTexture=target;
            var canvas=new GameObject("HUD").AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;canvas.planeDistance=1;
            var ui=new HudElements(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            var artwork=new InventoryArtwork();
            for(int i=0;i<3;i++)
            {
                var panel=ui.Panel(canvas.transform,"Состояние",new Vector2(i*480,0),new Vector2(480,320));
                var hud=new CombatHud(panel.transform,_=>{});
                var actor=new ActorState {id="hud-render",character=i==0?"Перезарядка":i==1?"Пустой магазин":"Нет запаса",weapon="pistol",health=100,mana=100};
                if(healthSamples) {actor.health=new[]{19.1f,.4f,0f}[i];actor.down=i==2;actor.character="Проверка здоровья";}
                WeaponAmmunition.Initialize(actor,true);
                WeaponAmmunition.Magazine(actor,"pistol").rounds=0;
                if(i==2) actor.inventory.Clear();
                if(i==0) actor.inventory.Add(SharedInventory.Create("grenade",5));
                hud.Refresh(actor,true,i==0?1.2f:(float?)null);
            }
            Canvas.ForceUpdateCanvases();
            var result=new Texture2D(1440,320,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;result.ReadPixels(new Rect(0,0,1440,320),0,0);result.Apply();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,healthSamples?"../../.reference/tests/combat-hud-health.png":"../../.reference/tests/combat-hud-reload.png")),result.EncodeToPNG());
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(result);}
        }
    }
}

