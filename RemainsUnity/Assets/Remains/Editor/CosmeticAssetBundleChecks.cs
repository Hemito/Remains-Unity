using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Separate fresh Editor invocation only. Builds diagnostic assets outside
    // Resources, loads a real Windows bundle through the public pack pipeline,
    // and never writes StreamingAssets, a player build or a user save.
    public static class CosmeticAssetBundleChecks
    {
        private const string PackId="check:wearable-asset-bundle";
        private const string ItemId="check:wearable-accessory";
        private const string VisualId="check:wearable-accessory-visual";
        private const string Slot="check:wearable-accessory-slot";
        private const string BundleName="wearable-accessory";
        private const string FixturePrefix="Assets/Remains/ValidationFixtures-CosmeticPack-";
        private const int AccessoryParts=100;
        [Serializable] private sealed class Order {public string[] mods;}
        [Serializable] private sealed class DefinitionPack
        {public int schema=1;public string[] slots;public Definition[] items;}
        [Serializable] private sealed class Definition {public string id,slot,visual,anatomy;}
        [Serializable] private sealed class Proof
        {
            public int schema=1;
            public bool success,explicitSyntheticFixture=true,originalRuntimeVerified=false;
            public string error,packId=PackId,itemId=ItemId,visualId=VisualId,slot=Slot;
            public string bundleSha256,sourceBankSha256,basePonySha256,loadedFingerprint,visualFingerprint,definitionsFingerprint;
            public string reportDirectory,fixtureDirectory;
            public int clips,frames,accessoryParts=AccessoryParts,idleAccessoryParts,idleSelectedParts,idleBareParts,selectedCapacity,bareCapacity;
            public int bundleVisuals,bundleIcons,loadedBundleCount,repeatedLoadCount;
            public bool beforeFreezeRegistration,sourceFilesPreserved,fixtureRemoved,repeatStable,worldPortraitPoolPassed,selectionOnly;
            public List<string> negativeRejects=new();
            public Dictionary<string,string> inputHashes=new(StringComparer.Ordinal);
            public string limits="EditMode real Windows AssetBundle/pack registration and actual world/portrait composition; no player process, full WeaponCatalog manifest, user saves or original-runtime comparison.";
        }

        // Do not add to warm ContractChecks.Run: this deliberately freezes an
        // isolated diagnostic registry, then the fresh Editor process exits.
        public static void RunModPack()
        {
            Require(WeaponCatalog.Rules==null && !WearableRenderCatalog.IsFrozen && !CosmeticCatalog.Load().IsFrozen,
                "нужен новый процесс Editor до WeaponCatalog.Load/PonyVisual.Prepare; регистры не оттаивают");
            string token=Guid.NewGuid().ToString("N"),fixture=FixturePrefix+token;
            string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            string repository=Path.GetFullPath(Path.Combine(project,".."));
            string output=Path.Combine(repository,".reference","tests","cosmetic-pack-native",token);
            Require(!Directory.Exists(Path.Combine(project,fixture)) && !Directory.Exists(output),"папка fixture уже существует");
            Directory.CreateDirectory(output);
            var proof=new Proof {reportDirectory=output,fixtureDirectory=fixture};
            string report=Path.Combine(output,"report.json");
            try
            {
                CaptureInputs(project,proof);
                string sourceResource="Original/Pony/Wearables/pip";
                var source=GameAssets.Load<TextAsset>(sourceResource);
                Require(source!=null,"нет проверенного исходного банка "+sourceResource);
                proof.sourceBankSha256=Hash(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));
                proof.basePonySha256=Hash(File.ReadAllBytes(Path.Combine(project,"Assets/Remains/Resources/Original/Pony/pony.json")));
                var sourceBank=StateJson.FromJson<WearableSourceBank>(source.text);
                var diagnostic=AccessoryBank(sourceBank);
                string bankJson=StateJson.ToJson(diagnostic);
                File.WriteAllText(Path.Combine(output,"diagnostic-bank.json"),bankJson);

                string folderName=Path.GetFileName(fixture);
                Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets/Remains",folderName)),"не создана isolated AssetDatabase fixture");
                string compiledPath=fixture+"/accessory.asset",iconPath=fixture+"/icons.asset";
                var compiled=WearableRenderCompiler.BuildAsset(bankJson,compiledPath,"synthetic validation:"+PackId);
                var icon=CreateIcon(iconPath);
                AssetDatabase.SaveAssets();
                var build=new AssetBundleBuild {assetBundleName=BundleName,assetNames=new[]{AssetDatabase.GetAssetPath(compiled),AssetDatabase.GetAssetPath(icon)}};
                string bundleDirectory=Path.Combine(output,"bundle");Directory.CreateDirectory(bundleDirectory);
                var manifest=BuildPipeline.BuildAssetBundles(bundleDirectory,new[]{build},
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);
                Require(manifest!=null,"BuildPipeline не создал настоящий Windows AssetBundle");
                string bundlePath=Path.Combine(bundleDirectory,BundleName);
                Require(File.Exists(bundlePath),"нет файла AssetBundle "+bundlePath);
                proof.bundleSha256=Hash(File.ReadAllBytes(bundlePath));
                InspectBundle(bundlePath,proof);
                // Build/unload may invalidate the authored native wrappers. Get
                // fresh AssetDatabase objects before duplicate/identity checks.
                compiled=AssetDatabase.LoadAssetAtPath<WearableRenderAsset>(compiledPath);
                icon=AssetDatabase.LoadAssetAtPath<InventoryIconAsset>(iconPath);
                Require(compiled!=null && icon!=null,"после build/unload не восстановлены авторские assets: "+compiledPath+", "+iconPath);
                compiled.Validate(PonyAnimationData.Library,PonyRenderAsset.SourceHash(),"authored bundle fixture:"+PackId);
                icon.CreateLookup();

                string mods=Path.Combine(output,"Mods"),pack=Path.Combine(mods,"accessory");Directory.CreateDirectory(pack);
                var definitions=new DefinitionPack {slots=new[]{Slot},items=new[]{new Definition {id=ItemId,slot=Slot,visual=VisualId,anatomy="pony"}}};
                string definitionJson=StateJson.ToJson(definitions);
                var texts=new ItemTextManifest {schema=1,items=new[]{new ItemTextDefinition {id=ItemId,name="Проверочный аксессуар",
                    description="Синтетический тестовый аксессуар: исходная графика, сто различимых частей только в idle0. Не исходный дизайн и не защита."}}};
                string textJson=StateJson.ToJson(texts);
                File.WriteAllText(Path.Combine(mods,"load-order.json"),StateJson.ToJson(new Order {mods=new[]{"accessory"}}));
                File.WriteAllText(Path.Combine(pack,"manifest.json"),StateJson.ToJson(new ModManifest {id=PackId,version="1",
                    dependencies=Array.Empty<string>(),cosmetics=new CosmeticPackEntry {definitions="definitions.json",texts="texts.json",visuals="accessory.bundle"}}));
                File.WriteAllText(Path.Combine(pack,"definitions.json"),definitionJson);
                File.WriteAllText(Path.Combine(pack,"texts.json"),textJson);
                File.Copy(bundlePath,Path.Combine(pack,"accessory.bundle"));
                var weapons=GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog");Require(weapons!=null,"нет базового WeaponAssetCatalog");
                string baseWeapons=weapons.CreateJson();
                var loaded=WeaponCatalogLoader.Load(baseWeapons,mods);
                Require(loaded.cosmetics.Count==1 && loaded.cosmetics[0].id==PackId && loaded.cosmetics[0].position==1 &&
                    loaded.manifest.packs.Count==2 && Hash(loaded.cosmetics[0].visuals)==proof.bundleSha256,
                    "loader потерял пакет, порядок или реальные bytes AssetBundle");
                proof.loadedFingerprint=loaded.fingerprint;
                int bundlesBefore=LoadedBundles();
                CosmeticContentMods.Prepare(loaded);
                proof.beforeFreezeRegistration=!CosmeticCatalog.Current.IsFrozen && !WearableRenderCatalog.IsFrozen;
                Require(proof.beforeFreezeRegistration,"пакет преждевременно заморозил регистрацию до Configure кодовых модов");
                proof.loadedBundleCount=LoadedBundles()-bundlesBefore;
                Require(proof.loadedBundleCount==1,"pack не загрузил ровно один настоящий AssetBundle");
                // Pure duplicate checks occur before freeze, after real bundle registration.
                Reject(()=>WearableRenderCatalog.Register(compiled,source:"negative duplicate "+PackId),VisualId,proof,"duplicate visual");
                string duplicateDefinition=StateJson.ToJson(new DefinitionPack {slots=Array.Empty<string>(),items=definitions.items});
                Reject(()=>CosmeticCatalog.Current.RegisterPack(duplicateDefinition,"negative duplicate "+PackId),ItemId,proof,"duplicate definition");
                Reject(()=>CosmeticItemArtwork.Register(ItemId,icon.CreateLookup()[ItemId],"negative duplicate "+PackId),ItemId,proof,"duplicate icon");
                var repeated=WeaponCatalogLoader.Load(baseWeapons,mods);
                Require(repeated.fingerprint==loaded.fingerprint,"неизменённые файлы изменили fingerprint");
                CosmeticContentMods.Prepare(repeated);proof.repeatedLoadCount++;
                Require(LoadedBundles()==bundlesBefore+1,"повторная Prepare повторно загрузила bundle");
                _=GameplayCodeMods.Compose(loaded.codeMods);
                proof.definitionsFingerprint=CosmeticCatalog.Current.Fingerprint;
                proof.visualFingerprint=WearableRenderCatalog.ContentFingerprint;
                PonyVisual.Prepare();ActorTypes.Load();
                Require(WearableRenderCatalog.TryGet(VisualId,out var actual),"нет зарегистрированного actual bundle visual "+VisualId);
                Require(CosmeticCatalog.Current.Get(ItemId).Slot==Slot && actual!=compiled &&
                    actual.SourceHash==compiled.SourceHash && actual.ContentFingerprint==compiled.ContentFingerprint,
                    "registry использует authoring SO вместо фактически загруженного bundle или потерял привязку");
                proof.clips=actual.Clips.Length;proof.frames=actual.Clips.Sum(clip=>clip.frames.Length);
                proof.idleAccessoryParts=actual.Clips.Single(clip=>clip.id=="idle").frames[0].parts.Length;
                Require(proof.clips==61 && proof.frames==347 && proof.idleAccessoryParts==AccessoryParts,
                    "не совпали полный набор поз или synthetic100 частей");
                var artwork=new InventoryArtwork();
                Require(artwork.TryGet(ItemId,out var sprite) && sprite!=null && sprite.texture!=null &&
                    sprite.pixelsPerUnit==200 && sprite.texture.width==342 && sprite.texture.height==357,
                    "InventoryArtwork не использует настоящий icon из bundle с native preview размером/PPU");
                Require(ItemTextCatalog.Load().Name(ItemId,"actual AssetBundle check")==texts.items[0].name &&
                    ItemTextCatalog.Load().Description(ItemId,"actual AssetBundle check")==texts.items[0].description,
                    "русские тексты пакета не применены");
                var actor=new ActorState {id="cosmetic-pack-native:selected",character="Проверка пакета",anatomy="pony",actorType="pony",
                    player=true,connected=true,level="hub",weapon=null,weaponInventoryVersion=1,loadoutVersion=1,
                    inventory=new List<ItemStack> {new() {id="cosmetic-pack-native:owned",definition=ItemId,count=1}}};
                string inventoryBefore=StateJson.ToJson(actor.inventory);float health=actor.health,mana=actor.mana;
                var position=actor.Position;string pose=actor.pose;float poseTime=actor.poseTime;
                Require(CosmeticEquipment.TryEquip(actor,actor.inventory[0].id,false,out var reason),"надевание не принято: "+reason);
                proof.selectionOnly=inventoryBefore==StateJson.ToJson(actor.inventory) && actor.health==health && actor.mana==mana &&
                    actor.Position==position && actor.pose==pose && actor.poseTime==poseTime;
                Require(proof.selectionOnly,"визуальное надевание изменило вещи, здоровье, ману, положение или body pose");
                var selected=PonyRenderSelection.PrepareEquipment(actor);var bare=actor.Copy();bare.cosmeticEquipment=null;
                var barePlan=PonyRenderSelection.PrepareEquipment(bare);
                proof.selectedCapacity=selected.Capacity;proof.bareCapacity=barePlan.Capacity;
                proof.idleSelectedParts=selected.Frames("idle")[0].Length;proof.idleBareParts=barePlan.Frames("idle")[0].Length;
                Require(proof.idleSelectedParts==proof.idleBareParts+AccessoryParts && proof.selectedCapacity>proof.bareCapacity &&
                    proof.selectedCapacity>=proof.idleSelectedParts,
                    $"synthetic100 не расширил действительный idle/pool: idle={proof.idleBareParts}→{proof.idleSelectedParts}, capacity={proof.bareCapacity}→{proof.selectedCapacity}");
                WearablePresentationChecks.CheckPoolProvisioning(actor);proof.worldPortraitPoolPassed=true;
                CosmeticContentMods.Prepare(repeated);proof.repeatedLoadCount++;
                proof.repeatStable=LoadedBundles()==bundlesBefore+1 && WearableRenderCatalog.ContentFingerprint==proof.visualFingerprint &&
                    CosmeticCatalog.Current.Fingerprint==proof.definitionsFingerprint;
                Require(proof.repeatStable,"повторная загрузка после freeze изменила регистр или native bundle");
                Reject(()=>CosmeticCatalog.Current.Register(new CosmeticDefinition("check:late",Slot,VisualId,"pony")),"уже используется",proof,"register after freeze");
                File.WriteAllText(Path.Combine(pack,"texts.json"),textJson.Replace("Проверочный аксессуар","Изменённый аксессуар"));
                var changed=WeaponCatalogLoader.Load(baseWeapons,mods);
                Require(changed.fingerprint!=loaded.fingerprint && changed.manifest.packs[1].fingerprint!=loaded.manifest.packs[1].fingerprint,
                    "изменение actual pack текста не меняет fingerprint");
                Reject(()=>CosmeticContentMods.Prepare(changed),"перезапустите",proof,"changed startup fingerprint");
                Require(WearableRenderCatalog.ContentFingerprint==proof.visualFingerprint,"отказ changed pack изменил действующую графику");
                proof.sourceFilesPreserved=InputsMatch(project,proof);Require(proof.sourceFilesPreserved,"fixture изменила исходные Assets");
                proof.success=true;
            }
            catch(Exception error) {proof.error=error.ToString();throw;}
            finally
            {
                try {RemoveFixture(project,fixture);proof.fixtureRemoved=true;}
                catch(Exception cleanup) {proof.success=false;proof.error=(proof.error??"")+"\nCleanup: "+cleanup;throw;}
                finally {File.WriteAllText(report,StateJson.ToJson(proof));}
            }
            Debug.Log($"CosmeticAssetBundleChecks PASS: real Windows bundle={proof.bundleSha256}, clips/frames={proof.clips}/{proof.frames}, " +
                $"actual pool={proof.bareCapacity}→{proof.selectedCapacity}, negatives={proof.negativeRejects.Count}, report={report}. " +
                "Synthetic EditMode fixture; исходный runtime и пользовательские сохранения не проверялись.");
        }

        private static WearableSourceBank AccessoryBank(WearableSourceBank source)
        {
            Require(source!=null && source.visualId=="pony.armor.pip","не подтверждён исходный pip bank");
            var glyph=source.templates.Single(template=>template.id=="159").parts.Single(part=>part.shape=="139");
            WearableSourcePart Part(int index)
            {var part=StateJson.Copy(glyph);part.path="/synthetic-source139/"+index;part.depthPath=new[]{index+1};return part;}
            var result=StateJson.Copy(source);result.visualId=VisualId;result.itemId=ItemId;
            result.textures=source.textures.Where(texture=>texture.key==glyph.texture).Select(texture=>StateJson.Copy(texture)).ToArray();
            result.templates=new[]{new WearableSourceTemplate {id="one",parts=new[]{Part(0)}},
                new WearableSourceTemplate {id="hundred",parts=Enumerable.Range(0,AccessoryParts).Select(Part).ToArray()}};
            foreach(var clip in result.clips) for(int f=0;f<clip.frames.Length;f++)
            {
                var frame=clip.frames[f];var placement=frame.instances.Single(instance=>instance.templateId=="159");
                frame.hiddenBasePartIndices=Array.Empty<int>();
                frame.instances=new[]{new WearableSourceInstance {templateId=clip.id=="idle" && f==0?"hundred":"one",
                    basePath="synthetic-validation-accessory",basePartIndices=Array.Empty<int>(),
                    matrix=(float[])placement.matrix.Clone(),color=(float[])placement.color.Clone(),depthPath=new[]{1000000}}};
            }
            return result;
        }
        private static InventoryIconAsset CreateIcon(string path)
        {
            var texture=GameAssets.Load<Texture2D>("Original/Pony/Wearables/preview-pip");
            Require(texture!=null && texture.width==342 && texture.height==357,"нет точного generated pip actor preview");
            var asset=ScriptableObject.CreateInstance<InventoryIconAsset>();AssetDatabase.CreateAsset(asset,path);
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),200,0,SpriteMeshType.FullRect);
            sprite.name="synthetic-accessory-preview";AssetDatabase.AddObjectToAsset(sprite,asset);
            var serialized=new SerializedObject(asset);var entries=serialized.FindProperty("entries");entries.arraySize=1;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("Id").stringValue=ItemId;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("Sprite").objectReferenceValue=sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);asset.CreateLookup();return asset;
        }
        private static void InspectBundle(string path,Proof proof)
        {
            var bundle=AssetBundle.LoadFromFile(path);Require(bundle!=null,"Windows bundle не читается native AssetBundle.LoadFromFile");
            try
            {
                var visuals=bundle.LoadAllAssets<WearableRenderAsset>();var icons=bundle.LoadAllAssets<InventoryIconAsset>();
                proof.bundleVisuals=visuals.Length;proof.bundleIcons=icons.Length;
                Require(visuals.Length==1 && visuals[0].VisualId==VisualId && icons.Length==1 && icons[0].CreateLookup().Keys.SequenceEqual(new[]{ItemId}),
                    "bundle втянул builtin WearableRenderAsset/duplicate IDs или потерял новый icon");
                visuals[0].Validate(PonyAnimationData.Library,PonyRenderAsset.SourceHash(),"actual Windows bundle:"+PackId);
            }
            finally {bundle.Unload(true);}
        }
        private static int LoadedBundles()=>AssetBundle.GetAllLoadedAssetBundles().Count();
        private static void CaptureInputs(string project,Proof proof)
        {
            foreach(string folder in new[]{"Assets/Remains/Resources/Original/Pony","Assets/Remains/Resources/Content"})
                foreach(string file in Directory.EnumerateFiles(Path.Combine(project,folder),"*",SearchOption.AllDirectories))
                    proof.inputHashes.Add(Path.GetRelativePath(project,file),Hash(File.ReadAllBytes(file)));
        }
        private static bool InputsMatch(string project,Proof proof)
        {
            var after=new Proof();CaptureInputs(project,after);
            return after.inputHashes.Count==proof.inputHashes.Count && proof.inputHashes.All(entry=>
                after.inputHashes.TryGetValue(entry.Key,out string hash) && hash==entry.Value);
        }
        private static void RemoveFixture(string project,string fixture)
        {
            string absolute=Path.GetFullPath(Path.Combine(project,fixture));
            string parent=Path.GetFullPath(Path.Combine(project,"Assets/Remains"));
            Require(fixture.StartsWith(FixturePrefix,StringComparison.Ordinal) && fixture.Length==FixturePrefix.Length+32 &&
                fixture.Substring(FixturePrefix.Length).All(Uri.IsHexDigit) && Path.GetDirectoryName(absolute)==parent,
                "небезопасная папка cleanup: "+fixture);
            if(AssetDatabase.IsValidFolder(fixture)) Require(AssetDatabase.DeleteAsset(fixture),"не удалена isolated AssetDatabase fixture: "+fixture);
            Require(!Directory.Exists(absolute),"после cleanup сохранилась isolated fixture: "+absolute);
        }
        private static string Hash(byte[] bytes)
        {using var digest=SHA256.Create();return BitConverter.ToString(digest.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static void Reject(Action operation,string fragment,Proof proof,string label)
        {
            try {operation();}
            catch(InvalidOperationException error) {Require(error.Message.Contains(fragment),"negative без контекста "+label+": "+error.Message);proof.negativeRejects.Add(label);return;}
            throw new InvalidOperationException("Cosmetic AssetBundle: negative принят: "+label);
        }
        private static void Require(bool condition,string detail)
        {if(!condition) throw new InvalidOperationException("Cosmetic AssetBundle: "+detail+".");}
    }
}
