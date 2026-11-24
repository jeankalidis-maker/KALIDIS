using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.SolidProfileFlex;
public sealed class SolidProfileFlex : IReviewedOperation {
 public string Name=>"solid_profile_flex";
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
  (XYZ min,XYZ max) Bounds(Solid s){
   var b=s.GetBoundingBox();var cs=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();
   return(new XYZ(cs.Min(q=>q.X),cs.Min(q=>q.Y),cs.Min(q=>q.Z)),new XYZ(cs.Max(q=>q.X),cs.Max(q=>q.Y),cs.Max(q=>q.Z)));
  }
  (XYZ min,XYZ max)[] Boxes(Element e)=>Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).Select(Bounds).OrderBy(b=>b.min.Z).ThenBy(b=>b.min.X).ThenBy(b=>b.min.Y).ToArray();
  double[] Size(Element e){var bs=Boxes(e);if(bs.Length==0)throw new InvalidOperationException("No physical solids");return new[]{ToMm(bs.Max(b=>b.max.X)-bs.Min(b=>b.min.X)),ToMm(bs.Max(b=>b.max.Y)-bs.Min(b=>b.min.Y)),ToMm(bs.Max(b=>b.max.Z)-bs.Min(b=>b.min.Z))};}
  object Report(FamilyInstance e){var o=((LocationPoint)e.Location).Point;double[] V(XYZ p)=>new[]{ToMm(p.X),ToMm(p.Y),ToMm(p.Z)};
   return new{id=e.Id.Value,dimensionsMm=Size(e),solidBoxes=Boxes(e).Select(b=>new{minMm=V(b.min-o),maxMm=V(b.max-o)}).ToArray()};}
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