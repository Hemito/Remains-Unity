using System;
using UnityEngine;
namespace Remains.Content
{
    public sealed class WearableRenderCatalogAsset : ScriptableObject
    {
        [SerializeField] private WearableRenderAsset[] profiles;
        public WearableRenderAsset[] Profiles=>profiles;
        public void Configure(WearableRenderAsset[] values)=>profiles=values??throw new ArgumentNullException(nameof(values));
    }
}
