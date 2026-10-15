using System;
using Remains.Domain;
using Remains.Networking;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // Actor messages by pieces (ActorStreams): the first message is whole, later ones carry the template and only the
    // changed cold texts; a receiver without the base asks for the full text and continues from it; the read actor
    // equals the sent one.
    //   batch -executeMethod Remains.Editor.ActorStreamChecks.RunBatch
    public static class ActorStreamChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var actor=new ActorState {id="check:stream",player=true,character="check",level="hub",x=1};
            actor.inventory.Add(new ItemStack {id="item:a",definition="caps",count=3});
            for(int i=0;i<200;i++) actor.inventoryAliases["item:alias-"+i]="item:a";
            var sender=new ActorPiecesSender();var receiver=new ActorPiecesReceiver();

            var first=sender.Next(actor);
            Require(first.json!=null && first.template==null,"Первое сообщение персонажа не целое.");
            Require(receiver.TryRead(first,out var read) && Same(read,actor),"Целое сообщение прочитано неверно.");

            actor.x+=1;actor.mana-=5;
            var moved=sender.Next(actor);
            Require(moved.json==null && moved.template!=null && Array.TrueForAll(moved.cold,part=>part==null),"Сдвиг персонажа передал холодные части.");
            Require(moved.template.Length<StateJson.ToJson(actor).Length/4,$"Шаблон сдвига слишком велик: {moved.template.Length}.");
            Require(receiver.TryRead(moved,out read) && Same(read,actor),"Сдвиг персонажа прочитан неверно.");

            actor.inventory[0].count=2;
            var spent=sender.Next(actor);
            int inventory=Array.IndexOf((string[])System.Linq.Enumerable.ToArray(ActorText.Cold),"inventory");
            for(int i=0;i<spent.cold.Length;i++) Require((spent.cold[i]!=null)==(i==inventory),$"Расход передал холодную часть {ActorText.Cold[i]}={spent.cold[i]!=null}.");
            Require(receiver.TryRead(spent,out read) && Same(read,actor),"Расход прочитан неверно.");

            // A receiver joining later lacks the base: it asks, gets the last state whole, then continues by pieces.
            var late=new ActorPiecesReceiver();
            actor.x+=1;var next=sender.Next(actor);
            Require(!late.TryRead(next,out _),"Сообщение без базы принято.");
            Require(sender.TryFull(actor.id,out var full) && late.TryRead(full,out read) && Same(read,actor),"Полное состояние по запросу неверно.");
            actor.x+=1;var after=sender.Next(actor);
            Require(late.TryRead(after,out read) && Same(read,actor),"После полного состояния части не продолжились.");
            Require(receiver.TryRead(next,out _) && receiver.TryRead(after,out read) && Same(read,actor),"Основной получатель потерял поток.");
            Debug.Log($"Сообщения персонажа по частям: PASS — целое первое, сдвиг шаблоном {moved.template.Length} символов вместо {StateJson.ToJson(actor).Length}, расход одной частью, запрос полного состояния.");
        }

        private static bool Same(ActorState a,ActorState b)=>StateJson.ToJson(a)==StateJson.ToJson(b);
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
