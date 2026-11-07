using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class WorldFactoryCreationChecks
    {
        public static void Run()
        {
            var legacy=new WorldState();
            Require(!WorldFactories.RequiresCreation(legacy,"factory:0"),"Старый неявный завод заменён генерацией.");
            legacy.rooms.Add(new WorldRoom {level="factory:1",definition="base:factory-connected-46-18"});
            string before=StateJson.ToJson(legacy);
            Require(!WorldFactories.RequiresCreation(legacy,"factory:0") &&
                !WorldFactories.RequiresCreation(legacy,"factory:1"),"Сохранённая авторская локация заменена генерацией.");
            Require(WorldFactories.RequiresCreation(legacy,"factory:2") &&
                WorldFactories.RequiresCreation(legacy,"factory:-1"),"Новый номер завода нельзя создать.");
            Require(!WorldFactories.RequiresCreation(legacy,"hub"),"Хаб принят за новый завод.");
            Require(StateJson.ToJson(legacy)==before,"Проверка существования изменила сохранённый мир.");
            var saved=StateJson.FromJson<WorldState>(before);
            Require(!WorldFactories.RequiresCreation(saved,"factory:0") &&
                !WorldFactories.RequiresCreation(saved,"factory:1"),"Старые привязки потеряны после сохранения.");
            var generated=NewGameWorld.Create(104);
            Require(!WorldFactories.RequiresCreation(generated,"factory:0") &&
                WorldFactories.RequiresCreation(generated,"factory:1"),"Новый мир потерял сохранённый завод или следующий номер.");
            generated.factories.Clear();
            Require(!WorldFactories.RequiresCreation(generated,"factory:0"),"Утраченная раскладка молча сгенерирована заново.");
            bool rejected=false;
            try {WorldFactories.Require(generated,"factory:0",NewGameWorld.FactoryDefinitionId,"missing");}
            catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Повреждённое сохранение принято без раскладки.");
            Debug.Log("Создание завода при переходе: PASS — старые локации сохранены, новые номера доступны, утраченная раскладка отвергнута.");
        }
        private static void Require(bool valid,string message)
        {if(!valid) throw new InvalidOperationException(message);}
    }
}
