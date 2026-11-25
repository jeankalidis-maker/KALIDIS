// Ports of KenLP/RevitMCPServer (MIT, Le Phu), revision fdf6efa23e57fe59effb8191666d04b4f73142a0.
// Adapted for Kalidis DTOs, explicit link scope and paginated read-only results.
// See THIRD-PARTY-KenLP.txt; no external server loaded.
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using System.Text.Json;
namespace Kalidis.AIEngine;
internal static class KenLPAdditionalPorts {
 internal static readonly string[] Actions={"find_element_by_unique_id","list_materials","list_sheets","list_spaces","list_rooms"};
 internal static object Read(Document doc,JsonElement p,string action){
  int limit=p.TryGetProperty("limit",out var l)?Math.Clamp(l.GetInt32(),1,2000):100;
  int offset=p.TryGetProperty("offset",out var o)?Math.Clamp(o.GetInt32(),0,1000000):0;
  long? level=p.TryGetProperty("levelId",out var lv)?lv.GetInt64():null;
  if(level.HasValue&&doc.GetElement(new ElementId(level.Value)) is not Level)throw new ArgumentException("Invalid level");
  double Area(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.SquareMeters);
  double Volume(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.CubicMeters);
  switch(action){
   case "find_element_by_unique_id":{
    string uid=p.GetProperty("uniqueId").GetString()??throw new ArgumentException("uniqueId");
    if(uid.Length>128||string.IsNullOrWhiteSpace(uid))throw new ArgumentException("Invalid UniqueId");
    Document target=doc;long? linkId=null;
    if(p.TryGetProperty("linkId",out var link)){
     var instance=doc.GetElement(new ElementId(link.GetInt64())) as RevitLinkInstance??throw new ArgumentException("Invalid link instance");
     target=instance.GetLinkDocument()??throw new InvalidOperationException("Link unloaded");
     linkId=instance.Id.Value;
    }
    var el=target.GetElement(uid);
    if(el==null)return new{ok=true,found=false,uniqueId=uid,linkId};
    return new{ok=true,found=true,foundIn=linkId.HasValue?"link":"host",linkId,id=el.Id.Value,uniqueId=el.UniqueId,name=el.Name,category=el.Category?.Name,typeId=el.GetTypeId().Value,documentTitle=target.Title,note="Linked IDs belong only to the specified linked document."};
   }
   case "list_materials":{
    var all=new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().OrderBy(x=>x.Name).ThenBy(x=>x.Id.Value).ToArray();
    var rows=all.Skip(offset).Take(limit).Select(m=>new{id=m.Id.Value,name=m.Name,materialClass=m.MaterialClass,materialCategory=m.MaterialCategory,color=m.Color is {IsValid:true} c?$"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}":null,transparency=m.Transparency,appearanceAssetId=m.AppearanceAssetId.Value}).ToArray();
    return new{ok=true,total=all.Length,offset,returned=rows.Length,hasMore=offset+rows.Length<all.Length,materials=rows};
   }
   case "list_sheets":{
    var all=new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().OrderBy(x=>x.SheetNumber).ThenBy(x=>x.Id.Value).ToArray();
    var rows=all.Skip(offset).Take(limit).Select(s=>new{id=s.Id.Value,number=s.SheetNumber,name=s.Name,placeholder=s.IsPlaceholder,viewportIds=s.GetAllViewports().Select(x=>x.Value).ToArray(),titleBlockIds=s.GetDependentElements(new ElementCategoryFilter(BuiltInCategory.OST_TitleBlocks)).Select(x=>x.Value).ToArray()}).ToArray();
    return new{ok=true,total=all.Length,offset,returned=rows.Length,hasMore=offset+rows.Length<all.Length,sheets=rows};
   }
   case "list_rooms":
   case "list_spaces":{
    var bic=action=="list_rooms"?BuiltInCategory.OST_Rooms:BuiltInCategory.OST_MEPSpaces;
    var all=new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().OfType<SpatialElement>().Where(x=>!level.HasValue||x.LevelId.Value==level.Value).OrderBy(x=>x.LevelId.Value).ThenBy(x=>x.Number).ThenBy(x=>x.Id.Value).ToArray();
    var rows=all.Skip(offset).Take(limit).Select(s=>new{id=s.Id.Value,name=s.Name,number=s.Number,levelId=s.LevelId.Value,levelName=s.Level?.Name,placed=s.Location!=null,areaM2=Area(s.Area),volumeM3=s is Room room?Volume(room.Volume):s is Space space?Volume(space.Volume):(double?)null,enclosed=s.Area>0}).ToArray();
    return new{ok=true,total=all.Length,offset,returned=rows.Length,hasMore=offset+rows.Length<all.Length,spatialElements=rows,note="Unplaced and unenclosed spatial elements included; volume depends on project settings."};
   }
  }
  throw new ArgumentException("Unknown additional port");
 }
}