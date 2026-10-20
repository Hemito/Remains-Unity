using System;
using System.Collections.Generic;
using System.IO;
using Remains.Persistence;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class SnapshotManifestChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            CheckReplicaManifestReuse();
            const string template="{\"мир\":\"🌕\",\"части\":1}";
            var input=Fixture(1024);
            var original=SnapshotManifest.Build(template,input);
            Require(original.Id=="m1:f929411effa172820851cd343a60525ade9b700301dcd18dc7cea5398085c8e7","Изменён исходный m1 digest.");
            Require(original.Id.StartsWith("m1:",StringComparison.Ordinal) && original.Id.Length==67,"Нет явной версии манифеста.");
            EqualFresh(template,input,original);
            var unchanged=SnapshotManifest.Build(new string(template.ToCharArray()),Copy(input),original);
            Require(unchanged.Id==original.Id && unchanged.HashedTexts==0 && unchanged.HashedBranches==0,
                "Неизменный снимок повторно хеширует тексты/ветви.");

            var changed=Copy(input);changed[0].Texts[777]="{\"x\":2,\"имя\":\"исправлено 🌕\"}";
            var corrected=SnapshotManifest.Build(template,changed,original);
            Require(corrected.Id!=original.Id && corrected.HashedTexts==1 && corrected.HashedBranches<=16,
                $"Одно исправление хеширует лишнее: тексты={corrected.HashedTexts}, ветви={corrected.HashedBranches}.");
            EqualFresh(template,changed,corrected);
            var clustered=Copy(input);
            for(int i=0;i<38;i++) clustered[0].Texts[i]="changed:"+i;
            var batch=SnapshotManifest.Build(template,clustered,original);
            Require(batch.Id=="m1:65b030ef08ccaf8b907772153b9c69a079fbb84b6233e2bfe558e107d33900ac","Пакетное обновление изменило прежний m1 digest.");
            Require(batch.HashedTexts==38 && batch.HashedBranches<110,
                $"Общие ветви хешируются повторно: {batch.HashedBranches} вместо менее 110 (прежний путь: 421).");
            EqualFresh(template,clustered,batch);
            var scattered=Copy(input);
            for(int i=0;i<38;i++) scattered[0].Texts[i*27]="scattered:"+i;
            var scatteredBatch=SnapshotManifest.Build(template,scattered,original);
            Require(scatteredBatch.HashedTexts==38 && scatteredBatch.HashedBranches<350,"Разнесённые изменения повторно хешируют общих предков.");
            EqualFresh(template,scattered,scatteredBatch);
            var batchUnchanged=SnapshotManifest.Build(template,Copy(clustered),batch);
            Require(batchUnchanged.Id==batch.Id && batchUnchanged.HashedTexts==0 && batchUnchanged.HashedBranches==0,"Пакетное обновление не переиспользуется.");
            var retemplate=SnapshotManifest.Build(template+" ",input,original);
            Require(retemplate.Id!=original.Id && retemplate.HashedTexts==1 && retemplate.HashedBranches==1,
                "Изменение шаблона затрагивает неизменные списки.");
            EqualFresh(template+" ",input,retemplate);

            var reordered=Copy(input);Swap(reordered[0].Keys,3,900);Swap(reordered[0].Texts,3,900);
            var orderManifest=SnapshotManifest.Build(template,reordered,original);
            Require(orderManifest.Id!=original.Id && orderManifest.HashedTexts==0,"Порядок ключей потерян или тексты хешируются повторно.");
            EqualFresh(template,reordered,orderManifest);
            var listOrder=Copy(input);(listOrder[0],listOrder[1])=(listOrder[1],listOrder[0]);
            var orderedLists=SnapshotManifest.Build(template,listOrder,original);
            Require(orderedLists.Id!=original.Id && orderedLists.HashedTexts==0,"Порядок списков потерян.");
            EqualFresh(template,listOrder,orderedLists);

            var removed=Copy(input);removed[0]=new SnapshotPartCache.PartSet(input[0].Name,
                Slice(input[0].Keys,1023),Slice(input[0].Texts,1023));
            var without=SnapshotManifest.Build(template,removed,original);
            Require(without.Id!=original.Id,"Удаление части не изменило манифест.");EqualFresh(template,removed,without);
            var added=Copy(removed);added[0]=new SnapshotPartCache.PartSet(input[0].Name,input[0].Keys,input[0].Texts);
            Require(SnapshotManifest.Build(template,added,without).Id==original.Id,"Восстановление части изменило исходный ID.");
            var foreign=SnapshotManifest.Build("другой мир",Fixture(7));
            Require(SnapshotManifest.Build(template,changed,foreign).Id==corrected.Id,"Другая база кеша изменила результат.");
            Require(SnapshotManifest.Build(template,Array.Empty<SnapshotPartCache.PartSet>()).Id!=original.Id,"Пустой состав совпал с полным.");

            // Inputs cannot mutate a published manifest or its later cache reads.
            var exposed=Copy(input);var owned=SnapshotManifest.Build(template,exposed);
            exposed[0].Keys[0]="изменённый ключ";exposed[0].Texts[1]="изменённый текст";exposed[1]=null;
            Require(SnapshotManifest.Build(template,Copy(input),owned).Id==original.Id,"Манифест хранит изменяемые входные массивы.");

            Invalid(()=>SnapshotManifest.Build(template,new[]{input[0],input[0]},original),"повтор списка");
            var duplicate=Copy(input);duplicate[0].Keys[1]=duplicate[0].Keys[0];
            Invalid(()=>SnapshotManifest.Build(template,duplicate,original),"повтор ключа");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("x",new[]{"k"},Array.Empty<string>())},original),"разные длины");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("x",new[]{"k"},new string[]{null})},original),"отсутствующий текст");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("",Array.Empty<string>(),Array.Empty<string>())},original),"пустое имя");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("x",new[]{""},new[]{"null"})},original),"пустой ключ");
            Invalid(()=>SnapshotManifest.Build("\ud800",input,original),"непарный surrogate в шаблоне");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("\udc00",new[]{"k"},new[]{"null"})},original),"surrogate в имени");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("x",new[]{"\ud800"},new[]{"null"})},original),"surrogate в ключе");
            Invalid(()=>SnapshotManifest.Build(template,new[]{new SnapshotPartCache.PartSet("x",new[]{"k"},new[]{"\ud800"})},original),"surrogate в тексте");
            Require(SnapshotManifest.Build(template,input,original).Id==original.Id,"Ошибка построения изменила предыдущий манифест.");

            // The same mutable arrays must be checked again after a prior successful build.
            var mutable=Copy(input);var mutableBase=SnapshotManifest.Build(template,mutable);
            mutable[0].Keys[1]=mutable[0].Keys[0];
            Invalid(()=>SnapshotManifest.Build(template,mutable,mutableBase),"дубликат в изменённом входном массиве");
            mutable[0].Keys[1]="\ud800";
            Invalid(()=>SnapshotManifest.Build(template,mutable,mutableBase),"surrogate в изменённом входном ключе");
            mutable[0].Keys[1]=input[0].Keys[1];mutable[0].Texts[1]=null;
            Invalid(()=>SnapshotManifest.Build(template,mutable,mutableBase),"null text при прежних ключах");
            mutable[0].Texts[1]="\ud800";
            Invalid(()=>SnapshotManifest.Build(template,mutable,mutableBase),"surrogate text при прежних ключах");
            Require(SnapshotManifest.Build(template,input,mutableBase).Id==original.Id,"Ошибочный fastpath изменил базу.");

            // Framing must distinguish boundaries and Unicode lengths, including names/keys containing delimiters.
            string a=SnapshotManifest.Build("т",new[]{new SnapshotPartCache.PartSet("a:b",new[]{"c"},new[]{"null"})}).Id;
            string b=SnapshotManifest.Build("т",new[]{new SnapshotPartCache.PartSet("a",new[]{"b:c"},new[]{"null"})}).Id;
            Require(a!=b,"Границы имени/ключа неоднозначны.");
            var emptyList=new[]{new SnapshotPartCache.PartSet("empty",Array.Empty<string>(),Array.Empty<string>())};
            Require(SnapshotManifest.Build(template,emptyList).Id!=SnapshotManifest.Build(template,Array.Empty<SnapshotPartCache.PartSet>()).Id,
                "Пустой список потерян в манифесте.");

            // Repeated scattered corrections exercise paths with shared parents and an unrelated prior cache.
            var random=new System.Random(104);var current=Copy(input);var manifest=original;
            for(int round=0;round<24;round++)
            {
                for(int update=0;update<5;update++) current[0].Texts[random.Next(current[0].Texts.Length)]="{\"round\":"+round+",\"update\":"+update+"}";
                manifest=SnapshotManifest.Build(template,current,manifest);EqualFresh(template,current,manifest);
            }
            Debug.Log($"Манифест снимка: PASS — версия, строгий UTF-8, порядок, исправления/удаления, другая база, изоляция; одно исправление: {corrected.HashedTexts} текст, {corrected.HashedBranches} ветвей.");
        }

        private static void CheckReplicaManifestReuse()
        {
            var world=new Remains.Domain.WorldState();
            for(int i=0;i<64;i++) world.actors.Add(new Remains.Domain.ActorState {id="manifest-actor-"+i,health=100});
            var before=RecoverySnapshot.FromWorld(world);var replica=WorldReplica.FromSnapshot(before);
            world.actors[0].x=2;world.revision++;
            var after=RecoverySnapshot.FromWorld(world);
            var delta=WorldDelta.Build(world.worldId,before.ReplicationId,before.Composition,after.ReplicationId,after.Composition);
            var assembled=replica.Apply(delta,before.CreateLiveWorld());
            Require(assembled.Json==after.Json && assembled.Id==after.Id,"Переиспользование манифеста изменило точный снимок");
            Require(assembled.Manifest.HashedTexts<10,"WorldReplica не передаёт предыдущий manifest и хеширует все неизменные части");
        }
        private static SnapshotPartCache.PartSet[] Fixture(int count)
        {
            var keys=new string[count];var texts=new string[count];
            for(int i=0;i<count;i++) {keys[i]="containers:"+i;texts[i]="{\"id\":\""+i+"\",\"x\":1}";}
            return new[]{new SnapshotPartCache.PartSet("containers",keys,texts),
                new SnapshotPartCache.PartSet("actors",new[]{"actors:игрок"},new[]{"{\"имя\":\"Луна 🌕\"}"})};
        }
        private static SnapshotPartCache.PartSet[] Copy(SnapshotPartCache.PartSet[] input)
        {
            var result=new SnapshotPartCache.PartSet[input.Length];
            for(int i=0;i<input.Length;i++) result[i]=new SnapshotPartCache.PartSet(input[i].Name,(string[])input[i].Keys.Clone(),(string[])input[i].Texts.Clone());
            return result;
        }
        private static string[] Slice(string[] source,int count) {var result=new string[count];Array.Copy(source,result,count);return result;}
        private static void Swap(string[] values,int a,int b)=>(values[a],values[b])=(values[b],values[a]);
        private static void EqualFresh(string template,SnapshotPartCache.PartSet[] input,SnapshotManifest manifest)=>
            Require(manifest.Id==SnapshotManifest.Build(template,input).Id,"Инкрементальный ID отличается от полной пересборки.");
        private static void Invalid(Action build,string context)
        {
            bool rejected=false;try {build();} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Не отклонены неверные данные: "+context);
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
