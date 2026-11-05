using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // The kinds of numbered factory (user 2026-11-06). Raider kinds 5 and 7 are the shooters connected so far.
    public static class FactoryProfileAuthoring
    {
        private const string Path="Assets/Remains/Resources/Content/FactoryProfiles.asset";
        public static void BuildBatch()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<FactoryProfileCatalog>(Path);
            bool created=catalog==null;
            if(created) catalog=ScriptableObject.CreateInstance<FactoryProfileCatalog>();
            catalog.profiles=new[]
            {
                new FactoryProfile {id="remains:factory:ordinary",title="Обычный завод",chance=2,
                    description="Заброшенный завод: в каждой комнате свои обитатели."},
                new FactoryProfile {id="remains:factory:raiders",title="Захвачен рейдерами",chance=3,monsterRooms=1,raiderRooms=8,robotRooms=1,
                    raiderVariants=new[]{1,2,3,4,5,5,5,7,7},raiderEquipment=4,dropForeignPlaced=.9f,ownCrawlers=.85f,
                    description="Банда обжила цеха. Почти все комнаты заняты рейдерами, у многих есть огнестрел."},
                new FactoryProfile {id="remains:factory:guarded",title="Нетронутый",chance=3,monsterRooms=1,raiderRooms=1,robotRooms=8,dropForeignPlaced=.9f,ownCrawlers=.85f,
                    description="Сюда почти никто не добрался. Цеха до сих пор охраняют боты и турели."},
                new FactoryProfile {id="remains:factory:infested",title="Заражённый",chance=2,monsterRooms=8,raiderRooms=1,robotRooms=1,dropForeignPlaced=.8f,
                    description="Завод отдан тварям: зомби, слизни и насекомые вместо людей и машин."},
            };
            catalog.Validate();
            if(created) AssetDatabase.CreateAsset(catalog,Path);
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            Debug.Log("Виды заводов: PASS — записано "+catalog.profiles.Length+".");
        }
    }
}
