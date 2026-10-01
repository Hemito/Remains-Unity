using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class CharacterContentChecks
    {
        public static void Run()
        {
            string root=Path.Combine(Path.GetTempPath(),"remains-character-content-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);var saves=new SaveStore(root);var gate=new CharacterContentGate();
            string path=Path.Combine(root,"character.json");
            var actor=new ActorState {character="test",health=71,weaponContent=WeaponCatalog.ManifestCopy()};
            actor.weaponContent.packs[0].version="old";
            string source=StateJson.ToJson(actor);File.WriteAllText(path,source);
            Require(!gate.TryPrepare(saves,"test",false,out _) && gate.Differences.Count>0,"Различия требуют решения");
            Require(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==1 && File.ReadAllText(path)==source,"До решения нет миграции и записи");
            gate.Reset();Require(File.ReadAllText(path)==source && gate.Differences.Count==0,"Отмена сохраняет файл");
            Require(!gate.TryPrepare(saves,"test",false,out _),"Отмена не сохраняет согласие");
            File.WriteAllText(path,source+" ");
            Reject(()=>gate.TryPrepare(saves,"test",true,out _),"изменился");
            File.WriteAllText(path,source);gate.Reset();gate.TryPrepare(saves,"test",false,out _);
            Require(gate.TryPrepare(saves,"test",true,out var prepared) && prepared.loadoutVersion==1 && prepared.health==71,"Подготовка только после решения");
            Require(File.ReadAllText(path)==source && Directory.GetFiles(Path.Combine(root,"Recovery")).Any(p=>File.ReadAllText(p)==source),"Точный исходник сохранён до миграции");
            int backups=Directory.GetFiles(Path.Combine(root,"Recovery")).Length;
            Require(gate.TryPrepare(saves,"test",false,out _),"Повтор Join после предупреждения хоста не теряет согласие");
            Require(Directory.GetFiles(Path.Combine(root,"Recovery")).Length==backups,"Повтор Join не повторяет миграцию и архивацию");
            gate.Reset();actor.weaponContent=null;source=StateJson.ToJson(actor);File.WriteAllText(path,source);
            Require(!gate.TryPrepare(saves,"test",false,out _),"Старый неизвестный каталог требует решения");
            Require(gate.TryPrepare(saves,"test",true,out _),"Неизвестный каталог разрешает явную попытку");
            gate.Reset();actor.weaponContent=WeaponCatalog.ManifestCopy();actor.weaponContent.schema=900;
            source=StateJson.ToJson(actor);File.WriteAllText(path,source);
            Require(!gate.TryPrepare(saves,"test",false,out _),"Неизвестный формат показан в сравнении");
            Reject(()=>gate.TryPrepare(saves,"test",true,out _),"не поддерживается");
            Require(File.ReadAllText(path)==source,"Отказ не меняет исходник");
            Debug.Log("CharacterContentChecks PASS: отмена, точное согласие, изменение файла, миграция, архив, неизвестный набор и формат. "+root);
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("CharacterContentChecks: "+reason);}
        private static void Reject(Action action,string reason)
        {
            try {action();} catch(InvalidDataException error) {Require(error.Message.Contains(reason),error.Message);return;}
            throw new InvalidOperationException("Не отклонён случай: "+reason);
        }
    }
}
