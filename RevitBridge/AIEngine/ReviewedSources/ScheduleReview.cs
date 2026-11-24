using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.ScheduleReview;
public sealed class ScheduleReview : IReviewedOperation {
 public string Name=>"schedule_review";
 public object Execute(Document d,JsonElement a){
  var s=d.GetElement(new ElementId(a.GetProperty("viewId").GetInt64())) as ViewSchedule??throw new ArgumentException("Schedule required");
  var def=s.Definition;var mode=a.GetProperty("mode").GetString();
  if(mode=="sort"){
   var key=a.GetProperty("fieldIndex").GetInt32();
   if(key<0||key>=def.GetFieldCount()||def.GetSortGroupFieldCount()!=0)throw new ArgumentException("Existing sort preserved or invalid field");
   var id=def.GetFieldId(key);if(!def.CanSortByField(id))throw new ArgumentException("Cannot sort by field");
   bool descending=a.TryGetProperty("descending",out var flag)&&flag.GetBoolean();
   def.AddSortGroupField(new ScheduleSortGroupField(id,descending?ScheduleSortOrder.Descending:ScheduleSortOrder.Ascending));
   if(def.GetSortGroupFieldCount()!=1||def.GetSortGroupField(0).FieldId!=id)throw new InvalidOperationException("Sort readback failed");
  }else if(mode=="filter"){
   var key=a.GetProperty("fieldIndex").GetInt32();
   if(key<0||key>=def.GetFieldCount()||def.GetFilterCount()!=0)throw new ArgumentException("Existing filter preserved or invalid field");
   var id=def.GetFieldId(key);var value=a.GetProperty("value").GetString()??throw new ArgumentException("String required");
   if(!def.CanFilterByValue(id))throw new ArgumentException("Cannot filter by value");
   def.AddFilter(new ScheduleFilter(id,ScheduleFilterType.Equal,value));
   if(def.GetFilterCount()!=1||def.GetFilter(0).FieldId!=id)throw new InvalidOperationException("Filter readback failed");
  }else if(mode=="group"){
   bool itemized=a.GetProperty("itemized").GetBoolean();def.IsItemized=itemized;
   if(def.IsItemized!=itemized)throw new InvalidOperationException("Grouping mismatch");
  }else if(mode!="audit")throw new ArgumentException("Unknown mode");
  d.Regenerate();
  return new{id=s.Id.Value,name=s.Name,fields=Enumerable.Range(0,def.GetFieldCount()).Select(i=>new{index=i,name=def.GetField(i).GetName()}),sortCount=def.GetSortGroupFieldCount(),filterCount=def.GetFilterCount(),itemized=def.IsItemized,note="Schedule presentation only; filtering can exclude quantities."};
 }
}