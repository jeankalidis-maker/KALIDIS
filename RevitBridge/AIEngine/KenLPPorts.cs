// Adapted from KenLP/RevitMCPServer, MIT, Copyright (c) 2026 Le Phu.
// Source revision fdf6efa23e57fe59effb8191666d04b4f73142a0.
// See THIRD-PARTY-KenLP.txt. Adapted to Kalidis JSON contract.
using System.Text.Json;
using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;
internal static class KenLPPorts {
 internal static readonly string[] Actions={"list_family_types","list_phases","list_view_templates","get_linked_files"};
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
   default:throw new ArgumentException("Unsupported KenLP port");
  }
 }
}