using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Networking;
using Remains.Persistence;
using UnityEngine;

namespace Remains.Editor
{
    // TD-31: a profile over the 64 KB Mirror string limit still joins; content history stays compact.
    public static class JoinCharacterChecks
    {
        public static void Run()
        {
            string json="{\"pad\":\""+new string('я',60000)+"\"}";   // ~120 KB UTF-8
            var join=new JoinMessage {character="c",characterUtf8=JoinCharacter.Encode(json)};
            Require(join.characterUtf8.Length>65534 && JoinCharacter.Decode(join.characterUtf8)==json,"Профиль больше 64 КБ не проходит кодирование подключения.");
            JoinCharacter.RequireFits(join,300000);
            bool rejected=false;
            try {JoinCharacter.RequireFits(join,100000);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Слишком большой профиль не отклонён до отправки.");

            // Repeated content changes add each earlier manifest once, without its per-asset list.
            ContentManifest saved=Manifest("a");var history=new List<ContentManifest> {Manifest("old"),Manifest("old")};
            ContentProvenance.Record(ref saved,ref history,Manifest("b"));
            Require(history.TrueForAll(m=>m.gameplayAssets!=null && m.gameplayAssets.Count==0),"История контента хранит списки ресурсов.");
            Require(history.FindAll(m=>m.fingerprint=="old").Count==1,"История контента не сжимает повторы.");
            Require(history.Exists(m=>m.fingerprint=="a" && m.packs.Count==1 && m.packs[0].id=="base"),"История контента потеряла прежний набор пакетов.");
            Debug.Log("Подключение крупного профиля: PASS — UTF-8 сверх 64 КБ, предел транспорта до отправки, сжатая история контента.");
        }
        private static ContentManifest Manifest(string fingerprint)=>new() {fingerprint=fingerprint,
            packs=new List<ContentPackRecord> {new() {id="base",version="1",fingerprint="p"}},
            gameplayAssets=new List<ContentAssetRecord> {new() {id="x",fingerprint="y"}}};
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
