using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.RebarInventory;
public sealed class RebarInventory : IReviewedOperation {
 public string Name=>"rebar_inventory";
 public object Execute(Document d,JsonElement a){
  var ids=a.GetProperty("ids").EnumerateArray().Select(v=>v.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>100)throw new ArgumentException("1-100 reinforcement IDs");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  var rows=ids.Select(id=>{
   var bar=d.GetElement(new ElementId(id)) as Rebar??throw new ArgumentException("Native rebar required");
   var type=d.GetElement(bar.GetTypeId()) as RebarBarType??throw new ArgumentException("Missing bar type");
   if(bar.NumberOfBarPositions>500)throw new ArgumentException("Too many positions");
   var lengths=new List<double>();
   for(int i=0;i<bar.NumberOfBarPositions;i++){
    if(!bar.DoesBarExistAtPosition(i))continue;
    lengths.Add(Mm(bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,i).Sum(c=>c.Length)));
   }
   if(lengths.Count!=bar.Quantity)throw new InvalidOperationException("Quantity mismatch");
   double diameter=Mm(type.BarNominalDiameter);
   double total=lengths.Sum()/1000;
   return new{id,hostId=bar.GetHostId().Value,typeId=type.Id.Value,diameterMm=diameter,quantity=lengths.Count,lengthsMm=lengths,totalLengthM=total,estimatedMassKg=total*Math.PI*Math.Pow(diameter/1000,2)/4*7850};
  }).ToArray();
  return new{elements=rows,totalBars=rows.Sum(x=>x.quantity),totalLengthM=rows.Sum(x=>x.totalLengthM),estimatedMassKg=rows.Sum(x=>x.estimatedMassKg),note="Centerline lengths; theoretical nominal steel mass at 7850 kg/m3; excludes waste and fabrication allowances."};
 }
}