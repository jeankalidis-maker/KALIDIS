using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.MaterialTakeoff;
public sealed class MaterialTakeoff : IReviewedOperation {
 public string Name=>"material_takeoff";
 public object Execute(Document d,JsonElement a){
  var ids=a.GetProperty("ids").EnumerateArray().Select(v=>v.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>200)throw new ArgumentException("1-200 IDs");
  var rows=new List<object>();var totals=new Dictionary<(long,bool),(string name,double area,double volume)>();var missing=new List<long>();
  foreach(var id in ids){
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing "+id);int found=0;
   foreach(bool paint in new[]{false,true}){
    foreach(var mid in e.GetMaterialIds(paint)){
     var m=d.GetElement(mid) as Material;if(m==null)continue;
     var area=UnitUtils.ConvertFromInternalUnits(e.GetMaterialArea(mid,paint),UnitTypeId.SquareMeters);
     var vol=paint?0:UnitUtils.ConvertFromInternalUnits(e.GetMaterialVolume(mid),UnitTypeId.CubicMeters);
     var key=(mid.Value,paint);totals.TryGetValue(key,out var old);totals[key]=(m.Name,old.area+area,old.volume+vol);
     rows.Add(new{elementId=id,materialId=mid.Value,name=m.Name,paint,areaM2=area,volumeM3=vol});found++;
    }
   }
   if(found==0)missing.Add(id);
  }
  return new{count=ids.Length,rows,totals=totals.Select(k=>new{materialId=k.Key.Item1,paint=k.Key.Item2,name=k.Value.name,areaM2=k.Value.area,volumeM3=k.Value.volume}).ToArray(),noMaterialQuantities=missing,note="Revit material quantities; painted area separate, no cost or procurement allowances; unsupported elements listed."};
 }
}