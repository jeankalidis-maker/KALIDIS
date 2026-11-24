// Adapted from KenLP/RevitMCPServer, MIT, Copyright (c) 2026 Le Phu.
// Source revision fdf6efa23e57fe59effb8191666d04b4f73142a0.
// See THIRD-PARTY-KenLP.txt. Adapted to Kalidis JSON contract.
using System.Text.Json;
using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;
internal static class KenLPPorts {
 internal static readonly string[] Actions={"list_family_types","list_phases","list_view_templates","get_linked_files","get_worksets","list_categories","get_doors","get_element_rooms","get_tags_in_view","get_linked_elements"};
 internal static object Read(Document doc,JsonElement p,string action){
  switch(action){
   case "list_family_types":{
    string familyName=p.TryGetProperty("familyName",out var f)?f.GetString()??"":"";
    string category=p.TryGetProperty("category",out var c)?c.GetString()??"":"";
    int limit=p.TryGetProperty("limit",out var l)?Math.Clamp(l.GetInt32(),1,5000):500;
    var query=new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>();
    if(!string.IsNullOrWhiteSpace(familyName))query=query.Where(s=>s.FamilyName==familyName);
    if(!string.IsNullOrWhiteSpace(category)){
     if(!Enum.TryParse<BuiltInCategory>(category,true,out var bic)||!Enum.IsDefined(bic))throw new ArgumentException("Invalid BuiltInCategory");
     query=query.Where(s=>s.Category?.BuiltInCategory==bic);
    }
    var familyTypes=query.OrderBy(s=>s.FamilyName).ThenBy(s=>s.Name).Take(limit).Select(s=>new{id=s.Id.Value,name=s.Name,familyName=s.FamilyName,familyId=s.Family?.Id.Value,category=s.Category?.Name,categoryEnum=s.Category?.BuiltInCategory.ToString(),isActive=s.IsActive}).ToArray();
    return new{ok=true,count=familyTypes.Length,familyTypes};
   }
   case "list_phases":{
    var phases=new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().OrderBy(p=>p.Id.Value).Select(ph=>new{id=ph.Id.Value,name=ph.Name}).ToArray();
    return new{ok=true,count=phases.Length,phases};
   }
   case "list_view_templates":{
    var viewTemplates=new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v=>v.IsTemplate).OrderBy(v=>v.Name).Select(t=>new{id=t.Id.Value,name=t.Name,viewType=t.ViewType.ToString()}).ToArray();
    return new{ok=true,count=viewTemplates.Length,viewTemplates};
   }
   case "get_linked_files":{
    var links=new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().Select(link=>{var linkDoc=link.GetLinkDocument();return new{id=link.Id.Value,name=link.Name,linkedDocTitle=linkDoc?.Title,linkedDocPath=linkDoc?.PathName,isLoaded=linkDoc!=null};}).ToArray();
    return new{ok=true,count=links.Length,links};
   }
   case "get_worksets":{
    if(!doc.IsWorkshared)return new{ok=true,isWorkshared=false,count=0,emptyCount=0,worksets=Array.Empty<object>()};
    var worksets=new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets().OrderBy(w=>w.Name).Select(ws=>{
     int count=new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(new ElementWorksetFilter(ws.Id)).GetElementCount();
     return new{id=ws.Id.IntegerValue,name=ws.Name,elementCount=count,isEmpty=count==0,isOpen=ws.IsOpen,isEditable=ws.IsEditable,isVisibleByDefault=ws.IsVisibleByDefault,owner=ws.Owner,isDefaultName=ws.Name=="Workset1"};
    }).ToArray();
    return new{ok=true,isWorkshared=true,count=worksets.Length,emptyCount=worksets.Count(w=>w.isEmpty),worksets};
   }
   case "list_categories":{
    var seen=new Dictionary<long,(string Name,string Enum,int Count)>();
    foreach(var el in new FilteredElementCollector(doc).WhereElementIsNotElementType()){
     var cat=el.Category;if(cat==null)continue;var key=cat.Id.Value;
     seen[key]=seen.TryGetValue(key,out var prev)?(prev.Name,prev.Enum,prev.Count+1):(cat.Name,cat.BuiltInCategory.ToString(),1);
    }
    var categories=seen.OrderByDescending(k=>k.Value.Count).Select(k=>new{id=k.Key,name=k.Value.Name,builtInCategory=k.Value.Enum,instanceCount=k.Value.Count}).ToArray();
    return new{ok=true,count=categories.Length,categories};
   }
   case "get_doors":{
    int limit=Limit(p,500,2000);
    var doors=new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Take(limit).Select(d=>{
     var pt=(d.Location as LocationPoint)?.Point;
     if(pt==null&&d.get_BoundingBox(null) is BoundingBoxXYZ box)pt=(box.Min+box.Max)*0.5;
     double? width=null;
     foreach(var element in new Element?[]{d,d.Symbol}){
      var w=element?.get_Parameter(BuiltInParameter.DOOR_WIDTH);
      if(w!=null&&w.HasValue&&w.StorageType==StorageType.Double&&w.AsDouble()>0){width=Mm(w.AsDouble());break;}
     }
     return new{id=d.Id.Value,name=d.Name,widthMm=width,pointMm=pt==null?null:Point(pt),levelName=(doc.GetElement(d.LevelId) as Level)?.Name,facing=new[]{d.FacingOrientation.X,d.FacingOrientation.Y},hand=new[]{d.HandOrientation.X,d.HandOrientation.Y},facingFlipped=d.FacingFlipped,handFlipped=d.HandFlipped};
    }).ToArray();
    return new{ok=true,count=doors.Length,doors};
   }
   case "get_element_rooms":{
    var ids=p.GetProperty("ids").EnumerateArray().Select(i=>i.GetInt64()).ToArray();
    if(ids.Length<1||ids.Length>500)throw new ArgumentException("1-500 IDs required");
    var fallback=doc.Phases.Cast<Phase>().LastOrDefault();
    var elements=ids.Select(id=>{
     var fi=doc.GetElement(new ElementId(id)) as FamilyInstance;
     Phase? phase=fi==null?null:(doc.GetElement(fi.CreatedPhaseId) as Phase??fallback);
     Autodesk.Revit.DB.Architecture.Room? room=null,fromRoom=null,toRoom=null;
     var errors=new List<string>();
     if(fi!=null){
      try{room=phase==null?fi.Room:fi.get_Room(phase);}catch{errors.Add("room");}
      try{fromRoom=phase==null?fi.FromRoom:fi.get_FromRoom(phase);}catch{errors.Add("fromRoom");}
      try{toRoom=phase==null?fi.ToRoom:fi.get_ToRoom(phase);}catch{errors.Add("toRoom");}
     }
     return new{id,phaseId=phase?.Id.Value,room=RoomInfo(room),fromRoom=RoomInfo(fromRoom),toRoom=RoomInfo(toRoom),unavailable=errors};
    }).ToArray();
    return new{ok=true,count=elements.Length,elements};
   }
   case "get_tags_in_view":{
    var vid=p.TryGetProperty("viewId",out var v)?new ElementId(v.GetInt64()):doc.ActiveView.Id;
    string category=p.TryGetProperty("category",out var c)?c.GetString()??"":"";
    if(!FilteredElementCollector.IsViewValidForElementIteration(doc,vid))throw new ArgumentException("Invalid view for iteration");
    var tags=new FilteredElementCollector(doc,vid).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().Select(tag=>{
     var ids=tag.GetTaggedLocalElementIds().Select(i=>i.Value).ToArray();
     var categories=ids.Select(i=>doc.GetElement(new ElementId(i))?.Category?.Name).Where(n=>n!=null).Distinct().ToArray();
     XYZ? pos=null;try{pos=tag.TagHeadPosition;}catch{}
     string? value=null;try{value=tag.TagText;}catch{}
     return new{tagId=tag.Id.Value,elementIds=ids,categories,hasLeader=tag.HasLeader,tagText=value,locationMm=pos==null?null:Point(pos)};
    }).Where(t=>string.IsNullOrEmpty(category)||t.categories.Contains(category,StringComparer.OrdinalIgnoreCase)).Take(Limit(p,500,2000)).ToArray();
    return new{ok=true,viewId=vid.Value,count=tags.Length,tags};
   }
   case "get_linked_elements":{
    var link=doc.GetElement(new ElementId(p.GetProperty("linkId").GetInt64())) as RevitLinkInstance??throw new ArgumentException("Invalid link");
    var linked=link.GetLinkDocument()??throw new InvalidOperationException("Link not loaded");
    var transform=link.GetTotalTransform();
    var col=new FilteredElementCollector(linked).WhereElementIsNotElementType();
    string category=p.TryGetProperty("category",out var c)?c.GetString()??"":"";
    if(category.Length>0){if(!Enum.TryParse<BuiltInCategory>(category,true,out var bic)||!Enum.IsDefined(bic))throw new ArgumentException("Invalid category");col.OfCategory(bic);}
    int limit=Limit(p,200,2000);
    var elements=col.Take(limit+1).Select(el=>new{id=el.Id.Value,uniqueId=el.UniqueId,name=el.Name,category=el.Category?.Name,categoryEnum=el.Category?.BuiltInCategory.ToString(),typeId=el.GetTypeId().Value,bboxInHostCoordsMm=HostBox(el.get_BoundingBox(null),transform)}).ToArray();
    return new{ok=true,linkId=link.Id.Value,linkName=link.Name,linkedDocTitle=linked.Title,count=Math.Min(elements.Length,limit),truncated=elements.Length>limit,elements=elements.Take(limit).ToArray()};
   }
   default:throw new ArgumentException("Unsupported KenLP port");
  }
 }
 private static int Limit(JsonElement p,int def,int max)=>p.TryGetProperty("limit",out var l)?Math.Clamp(l.GetInt32(),1,max):def;
 private static double Mm(double v)=>UnitUtils.ConvertFromInternalUnits(v,UnitTypeId.Millimeters);
 private static double[] Point(XYZ p)=>new[]{Mm(p.X),Mm(p.Y),Mm(p.Z)};
 private static object? RoomInfo(Autodesk.Revit.DB.Architecture.Room? room)=>room==null?null:new{id=room.Id.Value,name=room.Name,number=room.Number};
 // Transform all eight corners, unlike a two-corner transform which is incorrect for rotated links.
 private static object? HostBox(BoundingBoxXYZ? b,Transform t){
  if(b==null)return null;
  var corners=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select t.OfPoint(b.Transform.OfPoint(new XYZ(x,y,z)))).ToArray();
  return new{min=new[]{Mm(corners.Min(v=>v.X)),Mm(corners.Min(v=>v.Y)),Mm(corners.Min(v=>v.Z))},max=new[]{Mm(corners.Max(v=>v.X)),Mm(corners.Max(v=>v.Y)),Mm(corners.Max(v=>v.Z))}};
 }
}