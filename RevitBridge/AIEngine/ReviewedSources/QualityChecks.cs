using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.QualityChecks;
public sealed class QualityChecks : IReviewedOperation {
 public string Name=>"quality_checks";
 public object Execute(Document d,JsonElement a){
  var mode=a.GetProperty("mode").GetString();
  var cat=new ElementId(a.GetProperty("categoryId").GetInt64());
  var elems=new FilteredElementCollector(d).OfCategoryId(cat).WhereElementIsNotElementType().Take(10001).ToArray();
  if(elems.Length>10000)throw new ArgumentException("Category exceeds 10000 instances; scope further");
  if(mode=="duplicate_marks"){
   var groups=elems.Select(e=>new{id=e.Id.Value,mark=e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()}).Where(x=>!string.IsNullOrWhiteSpace(x.mark)).GroupBy(x=>x.mark,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>new{mark=g.Key,ids=g.Select(x=>x.id).ToArray()}).ToArray();
   return new{count=elems.Length,duplicates=groups};
  }
  if(mode=="missing_marks"){
   return new{count=elems.Length,ids=elems.Where(e=>e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK) is Parameter p&&string.IsNullOrWhiteSpace(p.AsString())).Select(e=>e.Id.Value).ToArray()};
  }
  if(mode=="unused_types"){
   var used=elems.Select(e=>e.GetTypeId()).ToHashSet();var types=new FilteredElementCollector(d).OfCategoryId(cat).WhereElementIsElementType().Take(10001).ToArray();
   if(types.Length>10000)throw new ArgumentException("Too many types");
   return new{count=types.Length,unused=types.Where(t=>!used.Contains(t.Id)).Select(t=>new{id=t.Id.Value,name=t.Name}).ToArray(),note="No direct placed instances; dependencies or nested usage not checked. Do not purge automatically."};
  }
  if(mode=="room_status"){
   return new{count=elems.Length,rooms=elems.OfType<Room>().Select(r=>new{id=r.Id.Value,number=r.Number,placed=r.Location!=null,enclosed=r.Area>0,areaM2=UnitUtils.ConvertFromInternalUnits(r.Area,UnitTypeId.SquareMeters)}).ToArray()};
  }
  if(mode=="hosts"){
   return new{count=elems.Length,instances=elems.OfType<FamilyInstance>().Select(e=>new{id=e.Id.Value,hostId=e.Host?.Id.Value,hostCategory=e.Host?.Category?.Name}).ToArray(),note="Null host is valid for unhosted families."};
  }
  throw new ArgumentException("Unknown mode");
 }
}