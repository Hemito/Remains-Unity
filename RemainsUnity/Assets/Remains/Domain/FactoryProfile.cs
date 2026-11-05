using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    // What sets a numbered factory apart (user 2026-11-06, PT-20261103-11, 27): who holds it. The profile changes who lives
    // there, never how hard the rules are: numbers stay equal alternatives (world-structure, «Индексные заводы»).
    [Serializable]
    public sealed class FactoryProfile
    {
        [InspectorName("Стабильный ID")] public string id;
        [InspectorName("Название")] public string title;
        [InspectorName("Описание"),TextArea] public string description;
        [Min(0),InspectorName("Частота среди заводов")] public int chance;
        // Rooms by family (original tipEnemy). All zero: the original rule of the main factory.
        [Min(0),InspectorName("Комнаты: монстры")] public int monsterRooms;
        [Min(0),InspectorName("Комнаты: рейдеры")] public int raiderRooms;
        [Min(0),InspectorName("Комнаты: роботы")] public int robotRooms;
        // Raider kinds of this place, any share by repetition. Empty: the original rule by the room's danger.
        [InspectorName("Виды рейдеров (1–9)")] public int[] raiderVariants;
        // Added to the room's danger for a raider's weapon only: its gang is armed better than the depth allows.
        [Min(0),InspectorName("Запас оружия рейдеров")] public int raiderEquipment;
        // More than half of a factory's creatures stand in its room templates, whatever the rooms' family: the share of the
        // ones of another family that are gone from this place.
        [Range(0,1),InspectorName("Чужие существа шаблонов: доля убранных")] public float dropForeignPlaced;
        // The crawling points of a raider or robot room are vermin by the original rule: the share its own family takes.
        [Range(0,1),InspectorName("Точки ползающих: доля своих")] public float ownCrawlers;

        public bool OriginalRooms=>monsterRooms+raiderRooms+robotRooms==0;
        public bool OriginalRaiders=>raiderVariants==null || raiderVariants.Length==0;
        public int EnemyType(Func<double> random)
        {
            if(OriginalRooms) return RandomEncounterRules.EnemyType(0,random);
            double roll=random()*(monsterRooms+raiderRooms+robotRooms);
            return roll<monsterRooms?0:roll<monsterRooms+raiderRooms?1:2;
        }
        public int RaiderVariant(double danger,Func<double> random)=>
            OriginalRaiders?RandomEncounterRules.RaiderVariant(danger,random):raiderVariants[(int)Math.Floor(random()*raiderVariants.Length)];
        public double RaiderWeaponDanger(double danger)=>danger+raiderEquipment;
        // 0 monsters, 1 raiders, 2 robots (tipEnemy).
        public int MainFamily=>monsterRooms>=raiderRooms && monsterRooms>=robotRooms?0:raiderRooms>=robotRooms?1:2;
        public static int Family(string behavior)=>behavior==NpcCreatureBehaviours.Raider?1:
            behavior is NpcCreatureBehaviours.Robobrain or NpcCreatureBehaviours.SpriteBot or NpcCreatureBehaviours.SpiderMine or
            NpcCreatureBehaviours.Roller or NpcCreatureBehaviours.Turret?2:0;
        public bool DropsPlaced(string behavior,Func<double> random)=>
            dropForeignPlaced>0 && Family(behavior)!=MainFamily && random()<dropForeignPlaced;
        public string Crawler(string unit,int enemyType,int parity,Func<double> random)
        {
            if(ownCrawlers<=0 || enemyType==0 || random()>=ownCrawlers) return unit;
            return enemyType==1?"raider":parity==0?"roller":"msp";
        }
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || chance<0 ||
                monsterRooms<0 || raiderRooms<0 || robotRooms<0 || raiderEquipment<0 ||
                dropForeignPlaced<0 || dropForeignPlaced>1 || ownCrawlers<0 || ownCrawlers>1)
                throw new InvalidOperationException($"Особенности завода {id}: неполное или неверное описание.");
            if(raiderVariants!=null) foreach(int variant in raiderVariants)
                if(variant<1 || variant>9) throw new InvalidOperationException($"Особенности завода {id}: нет вида рейдера {variant}.");
        }
        // One choice per world and number: the same number names the same kind of place for everyone in that world.
        public static FactoryProfile Choose(IReadOnlyList<FactoryProfile> profiles,ulong seed,int index)
        {
            int total=0;foreach(var profile in profiles) total+=profile.chance;
            if(total<=0) throw new InvalidOperationException("Особенности заводов: ни у одного нет частоты.");
            unchecked
            {
                ulong value=seed^((ulong)(long)index*0x9E3779B97F4A7C15UL)^0x5851F42D4C957F2DUL;
                value=(value^(value>>30))*0xBF58476D1CE4E5B9UL;value=(value^(value>>27))*0x94D049BB133111EBUL;value^=value>>31;
                int roll=(int)(value%(ulong)total);
                foreach(var profile in profiles) {roll-=profile.chance;if(roll<0) return profile;}
            }
            throw new InvalidOperationException("Особенности заводов: выбор вышел за частоты.");
        }
    }
}
