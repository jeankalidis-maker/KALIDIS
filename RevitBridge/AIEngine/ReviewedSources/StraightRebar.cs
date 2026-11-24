// Experimental native straight rebar creation, explicit geometry only, not structural design.
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Kalidis.AIEngine;
namespace Kalidis.StraightRebar;
public sealed class StraightRebar : IReviewedOperation {
 public string Name=>"straight_rebar";
 public object Execute(Document d,JsonElement a){
  var host=d.GetElement(new ElementId(a.GetProperty("hostId").GetInt64()))??throw new ArgumentException("Missing host");
  var data=RebarHostData.GetRebarHostData(host);
  if(data==null||!data.IsValidHost())throw new ArgumentException("Element is not currently a valid reinforcement host");
  double Mm(double v)=>UnitUtils.ConvertToInternalUnits(v,UnitTypeId.Millimeters);
  double ToMm(double v)=>UnitUtils.ConvertFromInternalUnits(v,UnitTypeId.Millimeters);
  XYZ Pt(string key){
   var v=a.GetProperty(key).EnumerateArray().Select(x=>x.GetDouble()).ToArray();
   if(v.Length!=3||v.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1000000))throw new ArgumentException("Invalid "+key);
   return new XYZ(Mm(v[0]),Mm(v[1]),Mm(v[2]));
  }
  var start=Pt("startMm");var end=Pt("endMm");
  if(start.DistanceTo(end)<Mm(100))throw new ArgumentException("Minimum test bar length 100mm");
  var b=host.get_BoundingBox(null)??throw new ArgumentException("Missing host bounds");
  var inv=b.Transform.Inverse;
  foreach(var p in new[]{start,end}){
   var q=inv.OfPoint(p);
   if(q.X<b.Min.X||q.X>b.Max.X||q.Y<b.Min.Y||q.Y>b.Max.Y||q.Z<b.Min.Z||q.Z>b.Max.Z)throw new ArgumentException("Endpoint outside host bounding box");
  }
  RebarBarType type;
  if(a.TryGetProperty("typeId",out var tid))type=d.GetElement(new ElementId(tid.GetInt64())) as RebarBarType??throw new ArgumentException("Invalid bar type");
  else{
   type=new FilteredElementCollector(d).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().FirstOrDefault()??(RebarBarType)d.GetElement(RebarBarType.CreateDefaultRebarBarType(d));
  }
  var direction=(end-start).Normalize();
  var normal=direction.CrossProduct(Math.Abs(direction.DotProduct(XYZ.BasisZ))>0.9?XYZ.BasisX:XYZ.BasisZ).Normalize();
  var bar=Rebar.CreateFromCurves(d,RebarStyle.Standard,type,null,null,host,normal,new List<Curve>{Line.CreateBound(start,end)},RebarHookOrientation.Left,RebarHookOrientation.Right,true,true);
  if(bar==null)throw new InvalidOperationException("Rebar creation returned null");
  d.Regenerate();
  if(bar.GetHostId()!=host.Id)throw new InvalidOperationException("Host mismatch");
  return new{id=bar.Id.Value,hostId=host.Id.Value,typeId=type.Id.Value,typeName=type.Name,nominalDiameterMm=ToMm(type.BarNominalDiameter),requestedLengthMm=ToMm(start.DistanceTo(end)),note="Experimental straight bar; bounding-box check only, no cover, anchorage, load or code compliance verification."};
 }
}