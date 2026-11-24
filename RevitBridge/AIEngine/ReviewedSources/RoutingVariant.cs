using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.RoutingVariant;
public sealed class RoutingVariant : IReviewedOperation {
 public string Name=>"routing_variant";
 public object Execute(Document d,JsonElement a){
  var original=d.GetElement(new ElementId(a.GetProperty("typeId").GetInt64())) as MEPCurveType??throw new ArgumentException("MEP curve type required");
  if(original is not PipeType&&original is not DuctType)throw new ArgumentException("Pipe or duct type required");
  var category=original is PipeType?BuiltInCategory.OST_PipeFitting:BuiltInCategory.OST_DuctFitting;
  var spec=a.GetProperty("rules").EnumerateArray().ToArray();
  if(spec.Length<1||spec.Length>4)throw new ArgumentException("1-4 rules");
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  var before=original.RoutingPreferenceManager;
  var groups=new[]{RoutingPreferenceRuleGroupType.Segments,RoutingPreferenceRuleGroupType.Elbows,RoutingPreferenceRuleGroupType.Junctions,RoutingPreferenceRuleGroupType.Transitions,RoutingPreferenceRuleGroupType.Unions};
  var prior=groups.ToDictionary(g=>g,g=>Enumerable.Range(0,before.GetNumberOfRules(g)).Select(i=>before.GetRule(g,i).MEPPartId.Value).ToArray());
  var copy=(MEPCurveType)original.Duplicate(a.GetProperty("name").GetString()??throw new ArgumentException("Name required"));
  var manager=copy.RoutingPreferenceManager;
  var seen=new HashSet<RoutingPreferenceRuleGroupType>();
  var rows=new List<object>();
  foreach(var r in spec){
   var group=r.GetProperty("group").GetString() switch{
    "Elbows"=>RoutingPreferenceRuleGroupType.Elbows,"Junctions"=>RoutingPreferenceRuleGroupType.Junctions,"Transitions"=>RoutingPreferenceRuleGroupType.Transitions,"Unions"=>RoutingPreferenceRuleGroupType.Unions,_=>throw new ArgumentException("Unsupported group")};
   if(!seen.Add(group)||manager.GetNumberOfRules(group)!=0)throw new ArgumentException("Existing rules preserved; only empty groups supported");
   var symbol=d.GetElement(new ElementId(r.GetProperty("symbolId").GetInt64())) as FamilySymbol??throw new ArgumentException("Family symbol required");
   if(symbol.Category?.Id.Value!=(long)category)throw new ArgumentException("Fitting category mismatch");
   double min=r.GetProperty("minMm").GetDouble(),max=r.GetProperty("maxMm").GetDouble();
   if(!double.IsFinite(min)||!double.IsFinite(max)||min<=0||max<min||max>2000)throw new ArgumentException("Invalid size interval");
   var rule=new RoutingPreferenceRule(symbol.Id,"Kalidis explicit fitting");
   rule.AddCriterion(new PrimarySizeCriterion(Mm(min),Mm(max)));
   manager.AddRule(group,rule);
   var actual=manager.GetRule(group,0);
   if(manager.GetNumberOfRules(group)!=1||actual.MEPPartId!=symbol.Id)throw new InvalidOperationException("Rule readback failed");
   rows.Add(new{group=group.ToString(),symbolId=symbol.Id.Value,family=symbol.Family.Name,minMm=min,maxMm=max});
  }
  d.Regenerate();
  foreach(var g in groups){
   var ids=Enumerable.Range(0,original.RoutingPreferenceManager.GetNumberOfRules(g)).Select(i=>original.RoutingPreferenceManager.GetRule(g,i).MEPPartId.Value);
   if(!ids.SequenceEqual(prior[g]))throw new InvalidOperationException("Original routing changed");
  }
  return new{id=copy.Id.Value,originalId=original.Id.Value,name=copy.Name,rules=rows,note="Rule assignment only; each fitting and size requires separate actual connectivity validation."};
 }
}