using System;
using System.Linq;
using UnityEngine;

namespace Remains.Runtime
{
    // Explicit diagnostic targets, not a content catalog or a gameplay rule.
    public sealed class FurnitureActionSpec
    {
        public readonly string key,code,definition,module;
        public readonly float x,y,width,height,mass;
        public readonly bool container,body;
        private FurnitureActionSpec(string key,string code,string definition,string module,float x,float y,float width,float height,float mass,bool container,bool body)
        {this.key=key;this.code=code;this.definition=definition;this.module=module;this.x=x;this.y=y;this.width=width;this.height=height;this.mass=mass;this.container=container;this.body=body;}
        public string Id=>"factory:0:original:plant:"+module+":"+code;
        public bool Continuation=>module=="27";
        public bool Transport=>!container;
        public bool FixedRejection=>key=="wallcab";
        public int FinalPhase=>Continuation?5:4;
        public int LandingPhase=>3;
        public string[] States=>key=="cup"?cupStates:FixedRejection?wallcabStates:container?containerStates:propStates;
        private static readonly string[] containerStates={"closed","open","empty","ui-closed","reopened"},
            propStates={"grounded","held","moved","released","returned"},
            cupStates={"closed","open","empty","ui-closed","reopened","too-heavy-rejected"},
            wallcabStates={"closed","open","empty","ui-closed","reopened","fixed-rejected"};
        private static readonly FurnitureActionSpec[] warehouse={
            new("safe","detVw68Tuzey1MqU","safe","40",151.2f,10.4f,1.2f,1.2f,64.8f,true,true),
            new("locker-upper","EHzteZ0u3qpHCu8Q","locker","40",147.2f,13.6f,1.6f,2.38f,0,true,false),
            new("locker-entry","WLoJrSYbx2hxtRHc","locker","40",120.8f,7.2f,1.6f,2.38f,0,true,false),
            new("couch","Z9yOwJPvLO0MySmN","couch","40",147.2f,.8f,1.6f,.4f,25.6f,false,true)};
        private static readonly FurnitureActionSpec[] continuation={
            new("cup","qgesiLTW2tpBNq9n","cup","27",182.4f,4,.94f,1.6f,60.16f,true,true),
            new("wallcab","Ru7orXdjq82U1G4B","wallcab","27",190,4.8f,.64f,.8f,0,true,false)};
        public static FurnitureActionSpec[] Warehouse()=>warehouse.ToArray();
        public static FurnitureActionSpec[] ContinuationSources()=>continuation.ToArray();
        public static FurnitureActionSpec Find(string key)=>warehouse.Concat(continuation).Single(s=>s.key==key);
        public Vector2 Start(bool driver)=>key switch {
            "safe"=>new(driver?149.5f:149.3f,10.4f),"locker-upper"=>new(driver?147.8f:148f,13.6f),
            "locker-entry"=>new(driver?119.5f:119.7f,7.2f),"couch"=>new(driver?145.55f:149.4f,.8f),
            "cup"=>new(driver?181.3f:184,4),"wallcab"=>new(driver?189.3f:190.6f,4),
            _=>throw new InvalidOperationException("Нет явной начальной позиции мебели: "+key)};
        public float ApproachX(bool driver)=>Start(driver).x+(key=="cup"?(driver?.1f:.15f):driver?.2f:-.2f);
        public (string definition,int count)[] Loot()=>key switch {
            "safe"=>new[]{("caps",12),("heal",1),("p10",12)},
            "cup"=>new[]{("caps",4),("pin",1),("kombu_comp",1)},
            "locker-upper" or "locker-entry" or "wallcab"=>new[]{("caps",3),("metal_comp",1)},
            _=>Array.Empty<(string,int)>()};
    }
}
