using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.ProfileFlex;
public sealed class ProfileFlex : IReviewedOperation {
 public string Name=>"profile_flex";
 public object Execute(Document d,JsonElement a){
  var inst=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as FamilyInstance??throw new ArgumentException("Family instance required");
  if(inst.Pinned||inst.Host!=null||inst.GroupId!=ElementId.InvalidElementId||inst.Location is not LocationPoint)throw new ArgumentException("Unhosted ungrouped point instance required");
  var original=inst.Symbol;
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double[] Vec(string key){var v=a.GetProperty(key).EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(v.Length!=3||v.Any(x=>!double.IsFinite(x)||Math.Abs(x)>30000))throw new ArgumentException(key);return v;}
  double[] Size(Element e){var b=e.get_BoundingBox(null)??throw new InvalidOperationException("No bounds");return new[]{ToMm(b.Max.X-b.Min.X),ToMm(b.Max.Y-b.Min.Y),ToMm(b.Max.Z-b.Min.Z)};}
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
  return new{originalId=inst.Id.Value,newId=copy.Id.Value,typeId=symbol.Id.Value,originalDimensionsMm=before,newDimensionsMm=actual,originalUnchanged=true};
 }
}