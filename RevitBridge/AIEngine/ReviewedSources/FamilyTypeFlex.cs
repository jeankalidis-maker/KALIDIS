using System.Text.Json;
using Autodesk.Revit.DB;
using Kalidis.AIEngine;
namespace Kalidis.FamilyTypeFlex;
public sealed class FamilyTypeFlex : IReviewedOperation {
 public string Name=>"family_type_flex";
 public object Execute(Document d,JsonElement a){
  var original=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as FamilyInstance??throw new ArgumentException("Family instance required");
  if(original.Host!=null||original.GroupId!=ElementId.InvalidElementId||original.Pinned||original.Location is not LocationPoint)throw new ArgumentException("Unhosted point instance required");
  var symbol=original.Symbol;
  string paramName=a.GetProperty("parameter").GetString()??throw new ArgumentException("Parameter required");
  string typeName=a.GetProperty("typeName").GetString()??throw new ArgumentException("Type name required");
  double Mm(double v)=>UnitUtils.ConvertToInternalUnits(v,UnitTypeId.Millimeters);
  double ToMm(double v)=>UnitUtils.ConvertFromInternalUnits(v,UnitTypeId.Millimeters);
  double Number(string k){double v=a.GetProperty(k).GetDouble();if(!double.IsFinite(v)||Math.Abs(v)>30000)throw new ArgumentException("Invalid "+k);return v;}
  Parameter Param(FamilySymbol s){
   var pp=s.GetParameters(paramName);
   if(pp.Count!=1||pp[0].StorageType!=StorageType.Double||pp[0].Definition.GetDataType()!=SpecTypeId.Length||pp[0].IsReadOnly)throw new ArgumentException("Unique writable length parameter required");
   return pp[0];
  }
  double Height(Element e){var b=e.get_BoundingBox(null)??throw new InvalidOperationException("Missing bounds");return ToMm(b.Max.Z-b.Min.Z);}
  var oldParam=ToMm(Param(symbol).AsDouble());var oldHeight=Height(original);
  var value=Number("valueMm");var expected=Number("expectedHeightDeltaMm");
  var off=a.GetProperty("offsetMm").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
  if(off.Length!=3||off.Any(x=>!double.IsFinite(x)||Math.Abs(x)>30000)||off.All(x=>Math.Abs(x)<0.1))throw new ArgumentException("Nonzero offset required");
  if(value<=0)throw new ArgumentException("Positive parameter required");
  var variant=(FamilySymbol)symbol.Duplicate(typeName);
  if(!Param(variant).Set(Mm(value)))throw new InvalidOperationException("Parameter edit refused");
  d.Regenerate();
  if(!variant.IsActive){variant.Activate();d.Regenerate();}
  var ids=ElementTransformUtils.CopyElement(d,original.Id,new XYZ(Mm(off[0]),Mm(off[1]),Mm(off[2])));
  var copies=ids.Select(i=>d.GetElement(i)).OfType<FamilyInstance>().ToArray();
  if(copies.Length!=1)throw new InvalidOperationException("Expected one copied instance");
  var copy=copies[0];copy.Symbol=variant;d.Regenerate();
  double actual=Height(copy)-oldHeight;
  if(Math.Abs(actual-expected)>0.1||Math.Abs(ToMm(Param(variant).AsDouble())-value)>0.1||Math.Abs(Height(original)-oldHeight)>0.1||Math.Abs(ToMm(Param(symbol).AsDouble())-oldParam)>0.1)throw new InvalidOperationException($"Flex mismatch; expected height change {expected} mm, got {actual} mm");
  return new{originalId=original.Id.Value,newId=copy.Id.Value,typeId=variant.Id.Value,parameter=paramName,originalValueMm=oldParam,newValueMm=value,originalHeightMm=oldHeight,newHeightMm=Height(copy),actualHeightDeltaMm=actual,originalUnchanged=true};
 }
}