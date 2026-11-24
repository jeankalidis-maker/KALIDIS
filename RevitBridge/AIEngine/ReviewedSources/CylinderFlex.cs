using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.CylinderFlex;
public sealed class CylinderFlex : IReviewedOperation {
 public string Name=>"cylinder_flex";
 public object Execute(Document d,JsonElement a){
  var inst=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as FamilyInstance??throw new ArgumentException("Family instance required");
  if(inst.Pinned||inst.Host!=null||inst.GroupId!=ElementId.InvalidElementId||inst.Location is not LocationPoint)throw new ArgumentException("Unhosted ungrouped point instance required");
  var original=inst.Symbol;
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double[] Vec(string key){var v=a.GetProperty(key).EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(v.Length!=3||v.Any(x=>!double.IsFinite(x)||Math.Abs(x)>30000))throw new ArgumentException(key);return v;}
  IEnumerable<Solid> Solids(GeometryElement? g,int depth=0){
   if(g==null||depth>8)yield break;
   foreach(var o in g){if(o is Solid s&&s.Faces.Size>0&&s.Volume>1e-9)yield return s;else if(o is GeometryInstance gi)foreach(var s2 in Solids(gi.GetInstanceGeometry(),depth+1))yield return s2;}
  }
  double[] Size(Element e){
   var solids=Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).ToArray();
   if(solids.Length!=1)throw new InvalidOperationException("Expected one physical solid");
   var cyl=solids[0].Faces.Cast<Face>().OfType<CylindricalFace>().ToArray();
   if(cyl.Length<1)throw new InvalidOperationException("No cylindrical face");
   var r=cyl[0].Radius;
   if(r<=0||cyl.Any(c=>Math.Abs(c.Radius-r)>Mm(0.01)||Math.Abs(c.Axis.Z)<0.99999))throw new InvalidOperationException("Only single upright cylinder");
   var caps=solids[0].Faces.Cast<Face>().OfType<PlanarFace>().Where(f=>Math.Abs(f.FaceNormal.Z)>0.99999).ToArray();
   if(caps.Length!=2)throw new InvalidOperationException("Expected two planar caps");
   double h=Math.Abs(caps[0].Origin.Z-caps[1].Origin.Z);
   if(h<=0||Math.Abs(solids[0].Volume-Math.PI*r*r*h)>Math.PI*r*r*h*1e-6)throw new InvalidOperationException("Not a full solid cylinder");
   return new[]{ToMm(2*r),ToMm(2*r),ToMm(h)};
  }
  object Report(FamilyInstance e){var dims=Size(e);return new{id=e.Id.Value,dimensionsMm=dims,radiusMm=dims[0]/2,heightMm=dims[2],volumeM3=UnitUtils.ConvertFromInternalUnits(Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).Sum(s=>s.Volume),UnitTypeId.CubicMeters)};}
  if(a.TryGetProperty("mode",out var mode)&&mode.GetString()=="audit")return Report(inst);
  Parameter P(FamilySymbol s,string key){var ps=s.GetParameters(key);if(ps.Count!=1||ps[0].IsReadOnly||ps[0].StorageType!=StorageType.Double||ps[0].Definition.GetDataType()!=SpecTypeId.Length)throw new ArgumentException("Unique writable length parameter: "+key);return ps[0];}
  var edits=a.GetProperty("parameters").EnumerateObject().ToArray();if(edits.Length<1||edits.Length>10)throw new ArgumentException("1-10 parameters");
  var expected=Vec("expectedDimensionsMm");var offset=Vec("offsetMm");if(offset.All(x=>Math.Abs(x)<0.1)||expected.Any(x=>x<=0))throw new ArgumentException("Positive dimensions and nonzero offset required");
  var before=Size(inst);var old=edits.ToDictionary(x=>x.Name,x=>P(original,x.Name).AsDouble());
  var symbol=(FamilySymbol)original.Duplicate(a.GetProperty("typeName").GetString()??throw new ArgumentException("Name required"));
  foreach(var item in edits){var val=item.Value.GetDouble();if(!double.IsFinite(val)||val<=0||val>30000)throw new ArgumentException("Invalid length");if(!P(symbol,item.Name).Set(Mm(val)))throw new InvalidOperationException("Set refused");}
  d.Regenerate();if(!symbol.IsActive){symbol.Activate();d.Regenerate();}
  var ids=ElementTransformUtils.CopyElement(d,inst.Id,new XYZ(Mm(offset[0]),Mm(offset[1]),Mm(offset[2])));
  var copies=ids.Select(i=>d.GetElement(i)).OfType<FamilyInstance>().ToArray();if(copies.Length!=1)throw new InvalidOperationException("Expected single copied instance");
  var copy=copies[0];copy.Symbol=symbol;d.Regenerate();var actual=Size(copy);var originalAfter=Size(inst);
  if(actual.Zip(expected,(x,y)=>Math.Abs(x-y)).Any(x=>x>0.2)||before.Zip(originalAfter,(x,y)=>Math.Abs(x-y)).Any(x=>x>0.2)||old.Any(x=>Math.Abs(P(original,x.Key).AsDouble()-x.Value)>Mm(0.1)))throw new InvalidOperationException("Geometry flex or original preservation mismatch: "+string.Join(",",actual));
  foreach(var item in edits)if(Math.Abs(ToMm(P(symbol,item.Name).AsDouble())-item.Value.GetDouble())>0.1)throw new InvalidOperationException("Parameter readback mismatch");
  return new{originalId=inst.Id.Value,newId=copy.Id.Value,typeId=symbol.Id.Value,originalDimensionsMm=before,newDimensionsMm=actual,originalUnchanged=true,originalSolids=Report(inst),newSolids=Report(copy)};
 }
}