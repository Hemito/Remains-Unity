using System;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Content
{
    // The one loader of game content by key (the former Resources path, e.g. "Content/WeaponCatalog"). Step 1 of
    // replacing Resources (user decision 2026-10-24, docs/history/assets-addressables-20261024.md): every load goes
    // through here while it is still backed by Resources; step 2 switches this class to Addressables, so the build
    // carries only the content the game data references instead of every file under a Resources folder.
    public static class GameAssets
    {
        // The asset, or null when the key names nothing of this type (callers report a missing asset themselves).
        public static T Load<T>(string key) where T:Object
        {
            if(string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Пустой ключ ассета игры.",nameof(key));
            return Resources.Load<T>(key);
        }
        public static T[] LoadAll<T>(string folder) where T:Object
        {
            if(string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Пустая папка ассетов игры.",nameof(folder));
            return Resources.LoadAll<T>(folder);
        }
        public static Request LoadAsync<T>(string key) where T:Object
        {
            if(string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Пустой ключ ассета игры.",nameof(key));
            return new Request(Resources.LoadAsync<T>(key));
        }
        // An asynchronous load: IsDone, then Asset (null when the key names nothing of the requested type).
        public sealed class Request
        {
            private readonly ResourceRequest request;
            internal Request(ResourceRequest request)=>this.request=request;
            public bool IsDone=>request.isDone;
            public Object Asset=>request.asset;
        }
    }
}
