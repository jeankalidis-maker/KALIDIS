using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.SheetReview;
public sealed class SheetReview : IReviewedOperation {
 public string Name=>"sheet_review";
 public object Execute(Document d,JsonElement a){
  var sheet=d.GetElement(new ElementId(a.GetProperty("sheetId").GetInt64())) as ViewSheet??throw new ArgumentException("Sheet required");
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  var mode=a.GetProperty("mode").GetString();
  if(mode=="move"||mode=="scale"){
   var vp=d.GetElement(new ElementId(a.GetProperty("viewportId").GetInt64())) as Viewport??throw new ArgumentException("Viewport required");
   if(vp.SheetId!=sheet.Id||vp.Pinned)throw new ArgumentException("Wrong sheet or pinned");
   if(mode=="move"){
    double x=a.GetProperty("xMm").GetDouble(),y=a.GetProperty("yMm").GetDouble();
    if(!double.IsFinite(x)||!double.IsFinite(y)||Math.Abs(x)>10000||Math.Abs(y)>10000)throw new ArgumentException("Invalid paper coordinate");
    var target=new XYZ(Mm(x),Mm(y),0);vp.SetBoxCenter(target);d.Regenerate();if(vp.GetBoxCenter().DistanceTo(target)>Mm(0.1))throw new InvalidOperationException("Position mismatch");
   }else{
    var view=(View)d.GetElement(vp.ViewId);int scale=a.GetProperty("scale").GetInt32();
    if(scale<1||scale>1000)throw new ArgumentException("Invalid scale");
    view.Scale=scale;d.Regenerate();if(view.Scale!=scale)throw new InvalidOperationException("Scale mismatch");
   }
  }else if(mode!="audit")throw new ArgumentException("Unknown mode");
  double[] Pt(XYZ p)=>new[]{ToMm(p.X),ToMm(p.Y)};
  var views=sheet.GetAllViewports().Select(id=>(Viewport)d.GetElement(id)).Select(v=>{
   var box=v.GetBoxOutline();
   return new{id=v.Id.Value,viewId=v.ViewId.Value,centerMm=Pt(v.GetBoxCenter()),minMm=Pt(box.MinimumPoint),maxMm=Pt(box.MaximumPoint),scale=((View)d.GetElement(v.ViewId)).Scale};
  }).ToArray();
  var schedules=new FilteredElementCollector(d,sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Select(s=>new{id=s.Id.Value,scheduleId=s.ScheduleId.Value,pointMm=Pt(s.Point)}).ToArray();
  var overlaps=new List<long[]>();
  for(int i=0;i<views.Length;i++)for(int j=i+1;j<views.Length;j++){
   var x=views[i];var y=views[j];if(x.minMm[0]<y.maxMm[0]&&y.minMm[0]<x.maxMm[0]&&x.minMm[1]<y.maxMm[1]&&y.minMm[1]<x.maxMm[1])overlaps.Add(new[]{x.id,y.id});
  }
  return new{sheetId=sheet.Id.Value,number=sheet.SheetNumber,views,schedules,viewportBoxOverlaps=overlaps,note="Viewport boxes only; labels, schedule extents and title block clearance not checked."};
 }
}