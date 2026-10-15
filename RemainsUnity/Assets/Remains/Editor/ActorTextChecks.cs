using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using Debug=UnityEngine.Debug;

namespace Remains.Editor
{
    // ActorText: template plus cold fields joins to exactly StateJson.ToJson(actor), for every actor of a real world
    // (-recoveryPerfWorld) or a built-in player with inventory, aliases and content history; null cold fields stay in place.
    //   batch -executeMethod Remains.Editor.ActorTextChecks.RunBatch [-recoveryPerfWorld <world.json>]
    public static class ActorTextChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            string input=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++) if(args[i]=="-recoveryPerfWorld") input=args[i+1];
            var world=input!=null?StateJson.FromJson<WorldState>(File.ReadAllText(input)):new WorldState();
            var player=new ActorState {id="check:actor-text",player=true,character="check",level="hub",x=1.5f};
            player.inventory.Add(new ItemStack {id="item:a",definition="caps",count=3});
            player.inventoryAliases["item:b"]="item:a";
            player.inventoryUses.Add(new InventoryUse {id="use:1"});
            world.actors.Add(player);
            int cold=0;
            foreach(var actor in world.actors)
            {
                string template=ActorText.Split(actor,out var parts);
                string joined=ActorText.Join(template,parts),expected=StateJson.ToJson(actor);
                if(joined!=expected)
                {
                    int at=0;while(at<Math.Min(joined.Length,expected.Length) && joined[at]==expected[at]) at++;
                    throw new InvalidOperationException($"Текст персонажа {actor.id} из частей отличается с символа {at}.");
                }
                if(actor.weaponContent==null && template.Contains("\"weaponContent\":null")==false)
                    throw new InvalidOperationException($"Пустое холодное поле персонажа {actor.id} не осталось в шаблоне.");
                foreach(string part in parts) if(part!=null) cold+=part.Length;
            }
            Debug.Log($"Текст персонажа по частям: PASS — {world.actors.Count} персонажей, склейка совпадает; холодных символов {cold}.");
        }
    }
}
