using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.RadialSketchTrial;
public sealed class RadialSketchTrial:IReviewedOperation{
 public string Name=>"radial_sketch_trial";
 static ExternalEvent? evt;static Handler? h;
 public object Execute(Document d,JsonElement a){
  if(a.GetProperty("mode").GetString()=="status")return new{status=h?.State??"idle",result=h?.Data};
  if(a.GetProperty("mode").GetString()!="start"||!a.TryGetProperty("executeTrial",out var p)||p.ValueKind!=JsonValueKind.True)throw new ArgumentException("Explicit trial required");
  if(h?.State is "queued" or "running")throw new InvalidOperationException("Already running");
  if(evt==null){h=new Handler();evt=ExternalEvent.Create(h);}
  h!.Project=d.PathName;h.State="queued";h.Data=null;
  if(evt.Raise()!=ExternalEventRequest.Accepted){h.State="rejected";throw new InvalidOperationException("Event refused");}
  return new{status="queued"};
 }
 sealed class Handler:IExternalEventHandler{
  internal string Project="",State="idle";internal object? Data;
  public string GetName()=>"Kalidis radial sketch trial";
  public void Execute(UIApplication ui){
   State="running";Document? fd=null;var trace=new List<object>();
   try{
    var d=ui.ActiveUIDocument?.Document??throw new InvalidOperationException("No active project");
    if(d.PathName!=Project||d.IsModifiable||d.IsFamilyDocument)throw new InvalidOperationException("Project changed");
    var template=@"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English\Metric Furniture.rft";
    fd=ui.Application.NewFamilyDocument(template);
    double M(double v)=>v/304.8;
    using(var t=new Transaction(fd,"Radial sketch trial")){
     t.Start();
     var fm=fd.FamilyManager;fm.NewType("R200");
     var plane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
     var ca=new CurveArray();ca.Append(Arc.Create(XYZ.Zero,M(200),0,Math.PI,XYZ.BasisX,XYZ.BasisY));ca.Append(Arc.Create(XYZ.Zero,M(200),Math.PI,Math.PI*2,XYZ.BasisX,XYZ.BasisY));
     var profile=new CurveArrArray();profile.Append(ca);var ex=fd.FamilyCreate.NewExtrusion(true,profile,plane,M(500));fd.Regenerate();
     var arcs=ex.Sketch.GetAllElements().Select(fd.GetElement).OfType<ModelCurve>().Where(c=>c.GeometryCurve is Arc).ToArray();
     trace.Add(new{sketch=ex.Sketch.Id.Value,arcs=arcs.Select(c=>new{id=c.Id.Value,kind=c.GetType().Name,radius=((Arc)c.GeometryCurve).Radius*304.8,reference=c.GeometryCurve.Reference?.ConvertToStableRepresentation(fd),parameters=c.Parameters.Cast<Parameter>().Where(p=>p.Definition.Name.Contains("Radius",StringComparison.OrdinalIgnoreCase)||p.Definition.Name.Contains("Raio",StringComparison.OrdinalIgnoreCase)).Select(p=>new{name=p.Definition.Name,readOnly=p.IsReadOnly,storage=p.StorageType.ToString()}).ToArray()}).ToArray()});
     if(arcs.Length!=2)throw new InvalidOperationException("Expected two sketch arcs");
     var view=new FilteredElementCollector(fd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v=>!v.IsTemplate&&v.ViewType==ViewType.FloorPlan);
     var fp=fm.AddParameter("Radius",GroupTypeId.Geometry,SpecTypeId.Length,false);fm.Set(fp,M(200));
     foreach(var arc in arcs){
      var c=(Arc)arc.GeometryCurve;var rf=c.Reference??new Reference(arc);
      var dim=fd.FamilyCreate.NewRadialDimension(view,rf,c.Evaluate(.5,true)*1.5);dim.FamilyLabel=fp;
     }
     fd.Regenerate();
     double Radius(){var ss=ex.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}).OfType<Solid>().Where(s=>s.Volume>1e-9).ToArray();return ss.SelectMany(s=>s.Faces.Cast<Face>()).OfType<CylindricalFace>().First().get_Radius(0).GetLength()*304.8;}
     var before=Radius();fm.NewType("R300");fm.Set(fp,M(300));fd.Regenerate();var after=Radius();trace.Add(new{before,after});
     if(Math.Abs(before-200)>.1||Math.Abs(after-300)>.1)throw new InvalidOperationException("Radius not driven");
     if(t.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Commit failed");
    }
    string file=Path.Combine(Path.GetDirectoryName(Project)!,"RadialVerified-"+Guid.NewGuid().ToString("N")+".rfa");
    fd.SaveAs(file,new SaveAsOptions{OverwriteExistingFile=false});fd.Close(false);fd=null;Data=new{file,trace};State="verified";
   }catch(Exception e){Data=new{error=e.ToString(),trace};State="failed";}
   finally{if(fd!=null&&fd.IsValidObject&&!fd.IsModifiable)fd.Close(false);}
  }
 }
}