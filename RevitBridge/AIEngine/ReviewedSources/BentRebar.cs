// Experimental planar bent reinforcement; explicit geometry, not structural design.
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.BentRebar;
public sealed class BentRebar : IReviewedOperation {
 public string Name=>"bent_rebar";
 public object Execute(Document d,JsonElement a){
  var host=d.GetElement(new ElementId(a.GetProperty("hostId").GetInt64()))??throw new ArgumentException("Missing host");
  if(RebarHostData.GetRebarHostData(host)?.IsValidHost()!=true)throw new ArgumentException("Invalid reinforcement host");
  var type=d.GetElement(new ElementId(a.GetProperty("typeId").GetInt64())) as RebarBarType??throw new ArgumentException("Invalid bar type");
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  XYZ Pt(JsonElement p){
   var v=p.EnumerateArray().Select(x=>x.GetDouble()).ToArray();
   if(v.Length!=3||v.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1000000))throw new ArgumentException("Invalid point");
   return new XYZ(Mm(v[0]),Mm(v[1]),Mm(v[2]));
  }
  var pts=a.GetProperty("pointsMm").EnumerateArray().Select(Pt).ToArray();
  if(pts.Length<3||pts.Length>20)throw new ArgumentException("3-20 vertices");
  var n=(pts[1]-pts[0]).CrossProduct(pts[2]-pts[1]);
  if(n.GetLength()<1e-8)throw new ArgumentException("First three points must define plane");
  n=n.Normalize();
  var bb=host.get_BoundingBox(null)??throw new ArgumentException("Missing host bounds");
  var inv=bb.Transform.Inverse;
  foreach(var p in pts){
   if(Math.Abs((p-pts[0]).DotProduct(n))>Mm(0.01))throw new ArgumentException("Nonplanar path");
   var q=inv.OfPoint(p);
   if(q.X<bb.Min.X||q.Y<bb.Min.Y||q.Z<bb.Min.Z||q.X>bb.Max.X||q.Y>bb.Max.Y||q.Z>bb.Max.Z)throw new ArgumentException("Vertex outside host bounds");
  }
  var curves=new List<Curve>();for(int i=1;i<pts.Length;i++){
   if(pts[i].DistanceTo(pts[i-1])<Mm(100))throw new ArgumentException("Segment too short for experiment");
   curves.Add(Line.CreateBound(pts[i-1],pts[i]));
  }
  var bar=Rebar.CreateFromCurves(d,RebarStyle.Standard,type,null,null,host,n,curves,RebarHookOrientation.Left,RebarHookOrientation.Right,true,true)??throw new InvalidOperationException("Creation failed");
  d.Regenerate();
  if(bar.GetHostId()!=host.Id)throw new InvalidOperationException("Host mismatch");
  var center=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  return new{id=bar.Id.Value,hostId=host.Id.Value,requestedSegments=curves.Count,actualCurves=center.Count,actualArcs=center.Count(c=>c is Arc),centerlineLengthMm=ToMm(center.Sum(c=>c.Length)),note="Experimental bar geometry only; no verification of cover, anchorage or loads."};
 }
}