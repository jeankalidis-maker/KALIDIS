using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.HookedSet;
public sealed class HookedSet : IReviewedOperation {
 public string Name=>"hooked_set";
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
  int count=a.GetProperty("count").GetInt32();var spacing=N("spacingMm");var minCover=N("minBoxCoverMm");if(count<2||count>30||spacing<Mm(25)||minCover<0)throw new ArgumentException("Invalid set");
  bar.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(count,spacing,true,true,true);
  d.Regenerate();
  if(bar.GetHostId()!=host.Id||bar.GetHookTypeId(0)!=hook.Id||bar.GetHookTypeId(1)!=hook.Id)throw new InvalidOperationException("Hook/host mismatch");
  var all=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  var plain=bar.GetCenterlineCurves(false,true,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  if(all.Sum(c=>c.Length)<=plain.Sum(c=>c.Length))throw new InvalidOperationException("Hook geometry not confirmed");
  var rb=bar.get_BoundingBox(null)??throw new InvalidOperationException("Missing rebar bounds");
  if(!box.Transform.IsIdentity||!rb.Transform.IsIdentity)throw new InvalidOperationException("Axis-aligned host and bar bounds required");
  var gaps=new[]{rb.Min.X-box.Min.X,rb.Min.Y-box.Min.Y,rb.Min.Z-box.Min.Z,box.Max.X-rb.Max.X,box.Max.Y-rb.Max.Y,box.Max.Z-rb.Max.Z};
  if(gaps.Min()<minCover||bar.Quantity!=count)throw new InvalidOperationException("Set count or box clearance mismatch");
  double[] zs=Enumerable.Range(0,count).Select(i=>bar.GetShapeDrivenAccessor().GetBarPositionTransform(i).Origin.Z).ToArray();
  if(zs.Skip(1).Select((v,i)=>Math.Abs(v-zs[i]-spacing)).Any(v=>v>Mm(.1)))throw new InvalidOperationException("Spacing mismatch");
  return new{quantity=bar.Quantity,spacingMm=UnitUtils.ConvertFromInternalUnits(spacing,UnitTypeId.Millimeters),minBoxClearanceMm=UnitUtils.ConvertFromInternalUnits(gaps.Min(),UnitTypeId.Millimeters),id=bar.Id.Value,hookTypeId=hook.Id.Value,hookAngleDegrees=135,withHooksMm=UnitUtils.ConvertFromInternalUnits(all.Sum(c=>c.Length),UnitTypeId.Millimeters),withoutHooksMm=UnitUtils.ConvertFromInternalUnits(plain.Sum(c=>c.Length),UnitTypeId.Millimeters),note="Experimental native set; AABB clearance only, not true concrete cover or clash/design certification."};
 }
}