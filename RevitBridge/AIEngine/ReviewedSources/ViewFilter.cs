using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.ViewFilter;
public sealed class ViewFilter : IReviewedOperation {
 public string Name=>"native_view_filter";
 public object Execute(Document d,JsonElement a){
  var mode=a.GetProperty("mode").GetString();
  T E<T>(string k) where T:Element=>d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as T??throw new ArgumentException(k);
  if(mode=="create"){
   var cat=new ElementId(a.GetProperty("categoryId").GetInt64());
   var val=a.GetProperty("contains").GetString()??throw new ArgumentException("Text required");
   if(val.Length<1||val.Length>100)throw new ArgumentException("Text length");
   var rule=ParameterFilterRuleFactory.CreateContainsRule(new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS),val);
   var f=ParameterFilterElement.Create(d,a.GetProperty("name").GetString(),new List<ElementId>{cat},new ElementParameterFilter(rule));
   d.Regenerate();return new{id=f.Id.Value,name=f.Name,categories=f.GetCategories().Select(x=>x.Value).ToArray()};
  }
  var v=E<View>("viewId");var filter=E<ParameterFilterElement>("filterId");
  if(v.IsTemplate||!v.AreGraphicsOverridesAllowed())throw new ArgumentException("View overrides unsupported");
  bool has()=>v.GetFilters().Contains(filter.Id);
  if(mode=="apply"){if(has())throw new ArgumentException("Already applied");v.AddFilter(filter.Id);if(!has())throw new InvalidOperationException("Apply failed");}
  else if(mode=="remove"){if(!has())throw new ArgumentException("Not applied");v.RemoveFilter(filter.Id);if(has())throw new InvalidOperationException("Remove failed");}
  else{
   if(!has())throw new ArgumentException("Apply filter first");
   if(mode=="color"){
    var rgb=a.GetProperty("rgb").EnumerateArray().Select(x=>x.GetByte()).ToArray();if(rgb.Length!=3)throw new ArgumentException("RGB required");
    var g=v.GetFilterOverrides(filter.Id);g.SetProjectionLineColor(new Color(rgb[0],rgb[1],rgb[2]));v.SetFilterOverrides(filter.Id,g);
    var c=v.GetFilterOverrides(filter.Id).ProjectionLineColor;if(c.Red!=rgb[0]||c.Green!=rgb[1]||c.Blue!=rgb[2])throw new InvalidOperationException("Color mismatch");
   }else if(mode=="visibility"){bool visible=a.GetProperty("visible").GetBoolean();v.SetFilterVisibility(filter.Id,visible);if(v.GetFilterVisibility(filter.Id)!=visible)throw new InvalidOperationException("Visibility mismatch");}
   else if(mode!="audit")throw new ArgumentException("Unknown mode");
  }
  d.Regenerate();
  return new{viewId=v.Id.Value,filterId=filter.Id.Value,applied=has(),visible=has()?(bool?)v.GetFilterVisibility(filter.Id):null};
 }
}