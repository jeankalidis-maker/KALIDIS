using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.HookedStirrup;
public sealed class HookedStirrup : IReviewedOperation {
 public string Name=>"hooked_stirrup";
 public object Execute(Document d,JsonElement a){
  var host=d.GetElement(new ElementId(a.GetProperty("hostId").GetInt64()))??throw new ArgumentException("Host required");
  if(RebarHostData.GetRebarHostData(host)?.IsValidHost()!=true)throw new ArgumentException("Invalid host");
  var type=d.GetElement(new ElementId(a.GetProperty("typeId").GetInt64())) as RebarBarType??throw new ArgumentException("Bar type required");
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double N(string k){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||Math.Abs(x)>1000000)throw new ArgumentException(k);return Mm(x);}
  var x=N("x");var y=N("y");var z=N("z");var w=N("widthMm");var l=N("lengthMm");
  if(w<Mm(300)||l<Mm(300))throw new ArgumentException("Minimum experimental size 300mm");
  var pts=new[]{new XYZ(x,y,z),new XYZ(x+w,y,z),new XYZ(x+w,y+l,z),new XYZ(x,y+l,z),new XYZ(x,y,z)};
  var box=host.get_BoundingBox(null)??throw new ArgumentException("Host bounds missing");
  foreach(var p in pts){var q=box.Transform.Inverse.OfPoint(p);if(q.X<box.Min.X||q.X>box.Max.X||q.Y<box.Min.Y||q.Y>box.Max.Y||q.Z<box.Min.Z||q.Z>box.Max.Z)throw new ArgumentException("Outside bounds");}
  var hook=RebarHookType.Create(d,135*Math.PI/180,10);hook.Style=RebarStyle.StirrupTie;
  var curves=new List<Curve>();for(int i=1;i<pts.Length;i++)curves.Add(Line.CreateBound(pts[i-1],pts[i]));
  var bar=Rebar.CreateFromCurves(d,RebarStyle.StirrupTie,type,hook,hook,host,XYZ.BasisZ,curves,RebarHookOrientation.Right,RebarHookOrientation.Right,true,true)??throw new InvalidOperationException("No bar");
  d.Regenerate();
  if(bar.GetHostId()!=host.Id||bar.GetHookTypeId(0)!=hook.Id||bar.GetHookTypeId(1)!=hook.Id)throw new InvalidOperationException("Hook/host mismatch");
  var all=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  var plain=bar.GetCenterlineCurves(false,true,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  if(all.Sum(c=>c.Length)<=plain.Sum(c=>c.Length))throw new InvalidOperationException("Hook geometry not confirmed");
  return new{id=bar.Id.Value,hookTypeId=hook.Id.Value,hookAngleDegrees=135,withHooksMm=UnitUtils.ConvertFromInternalUnits(all.Sum(c=>c.Length),UnitTypeId.Millimeters),withoutHooksMm=UnitUtils.ConvertFromInternalUnits(plain.Sum(c=>c.Length),UnitTypeId.Millimeters),note="Experimental native hooks; no anchorage, confinement, cover or structural design certification."};
 }
}