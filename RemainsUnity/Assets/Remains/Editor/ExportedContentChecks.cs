using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ExportedContentChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error){Debug.LogError("Проверка импортированного контента: "+error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var catalog=ExportedContentCatalog.Load();catalog.Validate();
            var source=JObject.Parse(File.ReadAllText(ContentPackage.Catalog));
            var expected=((JArray)source["definitions"]).OfType<JObject>()
                .Where(row=>new[]{"item","weapon","weapon1"}.Contains((string)row["category"])).ToArray();
            Require(catalog.items.Length==expected.Length,"число определений");
            foreach(var row in expected)
            {
                string key=(string)row["category"]+":"+(string)row["id"];
                var item=catalog.Require(key);
                Require(item.rawXml==(string)row["rawXml"],"исходные параметры "+key);
                Require(item.graphics.Length==((JArray)row["graphics"]).Count,"привязки графики "+key);
                Require(item.sounds.Length==((JArray)row["sounds"]).Count,"привязки звуков "+key);
                CheckBindings(item.graphics,(JArray)row["graphics"],key);
                CheckBindings(item.sounds,(JArray)row["sounds"],key);
            }
            Require(JToken.DeepEquals(JArray.Parse(catalog.weaponBundlesJson),source["weapons"]),"грейды, эффекты и параметры оружия");
            Require(JToken.DeepEquals(JArray.Parse(catalog.attachmentsJson),source["weaponAttachments"]),"крепления владельцев");
            // Player consumers share the same imported icon for inventory and world items.
            WeaponCatalog.Load();var artwork=new InventoryArtwork();var texts=ItemTextCatalog.Load();
            File.WriteAllText("../.reference/content-existing-weapons.json",ExportedWeaponAdapter.ExistingAliasReport(catalog,GameAssets.Load<WeaponAssetCatalog>("Content/WeaponCatalog")).ToString());
            foreach(string id in new[]{"p10mm","p9mm","p10","p10_1","energ","medx"})
            {
                Require(catalog.TryIcon(id,0,out var original),"исходная иконка "+id);
                var authored=GameAssets.Load<InventoryIconAsset>("Original/Inventory/icons").CreateLookup();
                var expectedIcon=authored.TryGetValue(id,out var previous)?previous:original;
                Require(artwork.TryGet(id,out var inventory)&&inventory==expectedIcon,"иконка потребителя "+id);
                Require(artwork.Require(id)==expectedIcon,"иконка лежащего предмета "+id);
                var item=SharedInventory.Create(id,2);
                Require(item.weaponState==null&&new StandardItemActions().Action(item)==StandardItemActions.Hold,"обычный предмет без фиктивной механики "+id);
            }
            Require(texts.Name("p10mm","проверка импорта")=="10-мм пистолет","исходное русское название");
            Require(catalog.TryIcon("weapon:p10mm",1,out var sharedGrade)&&catalog.TryIcon("weapon:p10mm",0,out var baseGrade)&&sharedGrade==baseGrade,"общий рисунок грейдов");
            var formal=catalog.CreateFormalItem("weapon:grenade",0,1);
            Require(!WeaponInventory.IsEquippable(formal)&&new StandardItemActions().Action(formal)==StandardItemActions.Hold,"исходная граната не включает действие по совпадению старого ID");
            var ammoBinding=catalog.Require("item:p10").graphics.Single(binding=>binding.role=="item-image");
            Require(ammoBinding.frame==2,"п10 использует исходный кадр 2");
            Require(catalog.TryIcon("weapon:p10mm:grade:1",0,out var qualified)&&qualified==sharedGrade,"явный ID грейда");
            // Check each authored sprite's geometry without loading every texture into the runtime catalog.
            int frames=0;
            foreach(var resource in catalog.resources)
                foreach(var frame in resource.frames??Array.Empty<ExportedFrame>())
                {
                    string path="Assets/Remains/Resources/"+frame.spriteResource+".asset";
                    var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    Require(sprite!=null,"спрайт "+resource.id+"/"+frame.frame);
                    Require(Mathf.Abs(sprite.pixelsPerUnit-50*frame.zoom)<.001f,"PPU "+path);
                    var expectedPivot=new Vector2(frame.sourceOrigin.x*frame.zoom,sprite.rect.height-frame.sourceOrigin.y*frame.zoom);
                    Require(Vector2.Distance(sprite.pivot,expectedPivot)<.02f,"регистрация "+path);
                    frames++;
                }
            Debug.Log($"PASS импортированного контента: {catalog.items.Length} определений, {catalog.resources.Length} ресурсов, {frames} кадров; исходные параметры, грейды, крепления, иконки инвентаря/мира, русское название и формальные предметы проверены.");
        }
        private static void Require(bool condition,string detail)
        {if(!condition)throw new InvalidOperationException("Не выполнено: "+detail);}
        private static void CheckBindings(ExportedBinding[] actual,JArray expected,string key)
        {
            for(int i=0;i<actual.Length;i++)
            {
                var row=expected[i];var binding=actual[i];
                bool Same(string actualValue,string sourceValue)=>(actualValue??"")==(sourceValue??"");
                Require(Same(binding.role,(string)row["role"])&&Same(binding.resourceId,(string)row["resource"])&&
                    Same(binding.rule,(string)row["rule"])&&Same(binding.label,(string)row["label"])&&binding.frame==((int?)row["frame"]??0)&&
                    Same(binding.metadataXml,(string)row["metadataXml"]),"содержание привязки "+key+"/"+i);
            }
        }
    }
}
