namespace Remains.Content
{
    // The port's weapon for an original weapon and grade: the hand-made ones keep their own IDs for grade 0 (pistol is
    // p10mm, energy lasp, spear, grenade), every other is its formal item ID (FirearmAuthoring: weapon:<id>:grade:<n>).
    public static class OriginalWeaponIds
    {
        public static string HandMade(string id,int grade)=>grade==0?id switch {"p10mm"=>"pistol","lasp"=>"energy","spear"=>"spear","grenade"=>"grenade",_=>null}:null;
        public static string Runtime(string id,int grade)=>HandMade(id,grade)??$"weapon:{id}:grade:{grade}";
    }
}
