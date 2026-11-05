using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    // The kinds of factory a world's numbers can be (FactoryProfile). The main factory is always the first entry.
    public sealed class FactoryProfileCatalog : ScriptableObject
    {
        public const string Resource="Content/FactoryProfiles";
        [InspectorName("Виды заводов (первый — основной)")] public FactoryProfile[] profiles;
        public FactoryProfile Main=>profiles[0];
        public FactoryProfile For(ulong seed,int index)=>index==0?Main:FactoryProfile.Choose(profiles,seed,index);
        // A saved factory keeps the kind it was created with; one from before kinds existed (the main one) has none.
        public FactoryProfile Saved(WorldFactory factory)
        {
            if(string.IsNullOrEmpty(factory.profile)) return Main;
            foreach(var profile in profiles) if(profile.id==factory.profile) return profile;
            Debug.LogWarning($"Завод {factory.level}: неизвестный вид {factory.profile} (мод убран?). Установленное население не меняется, новые комнаты — как у основного.");
            return Main;
        }
        public void Validate()
        {
            if(profiles==null || profiles.Length==0) throw new InvalidOperationException("Виды заводов: каталог пуст.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var profile in profiles)
            {
                profile.Validate();
                if(!ids.Add(profile.id)) throw new InvalidOperationException("Виды заводов: повторный ID "+profile.id);
            }
        }
        public static FactoryProfileCatalog Load()
        {
            var catalog=GameAssets.Load<FactoryProfileCatalog>(Resource)??
                throw new InvalidOperationException("Нет каталога видов заводов "+Resource+"; запустите FactoryProfileAuthoring.BuildBatch.");
            catalog.Validate();return catalog;
        }
    }
}
