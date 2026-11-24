using Autodesk.Revit.DB;
namespace Kalidis.AIEngine;
// Constrain an axis-aligned rectangular extrusion in a family document.
internal static class FamilyProfileConstraints {
 internal static object Bind(Document fd,Extrusion form,XYZ[] pts,Dictionary<string,FamilyParameter> parameters,string widthName,string depthName){
  if(pts.Length!=4||!form.IsSolid||widthName==depthName||parameters.ContainsKey(widthName)||parameters.ContainsKey(depthName))throw new ArgumentException("Unique parameters and rectangular solid required");
  double x0=pts.Min(p=>p.X),x1=pts.Max(p=>p.X),y0=pts.Min(p=>p.Y),y1=pts.Max(p=>p.Y),z=pts[0].Z;
  double tol=UnitUtils.ConvertToInternalUnits(0.01,UnitTypeId.Millimeters);
  bool Near(double a,double b)=>Math.Abs(a-b)<tol;
  if(x1-x0<tol||y1-y0<tol||pts.Any(p=>!(Near(p.X,x0)||Near(p.X,x1))||!(Near(p.Y,y0)||Near(p.Y,y1))))throw new ArgumentException("Axis-aligned rectangle required");
  var view=new FilteredElementCollector(fd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v=>!v.IsTemplate&&v.ViewType==ViewType.FloorPlan)??throw new InvalidOperationException("Family floor plan required");
  var fm=fd.FamilyManager;
  double span=UnitUtils.ConvertToInternalUnits(3000,UnitTypeId.Millimeters);
  fd.Regenerate();
  var solids=form.get_Geometry(new Options{ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine}).OfType<Solid>().Where(s=>s.Volume>0).ToArray();
  if(solids.Length!=1)throw new InvalidOperationException("Expected single extrusion solid");
  var faces=solids[0].Faces.Cast<Face>().OfType<PlanarFace>().ToArray();
  ReferencePlane PlaneAt(bool x,double coordinate,string name,bool fixedPlane){
   XYZ a=x?new XYZ(coordinate,-span,z):new XYZ(-span,coordinate,z);
   XYZ b=x?new XYZ(coordinate,span,z):new XYZ(span,coordinate,z);
   var p=fd.FamilyCreate.NewReferencePlane(a,b,XYZ.BasisZ,view);p.Name=name;
   if(fixedPlane)p.Pinned=true;
   return p;
  }
  Reference FaceAt(bool x,double coordinate){
   var f=faces.Single(f=>Math.Abs(x?f.FaceNormal.X:f.FaceNormal.Y)>0.999&&Near(x?f.Origin.X:f.Origin.Y,coordinate));
   return f.Reference??throw new InvalidOperationException("Missing side face reference");
  }
  // Capture stable references before changing the document.
  var faceRefs=new[]{FaceAt(true,x0),FaceAt(true,x1),FaceAt(false,y0),FaceAt(false,y1)};
  var planes=new[]{PlaneAt(true,x0,widthName+"_Min",true),PlaneAt(true,x1,widthName+"_Max",false),PlaneAt(false,y0,depthName+"_Min",true),PlaneAt(false,y1,depthName+"_Max",false)};
  fd.Regenerate();
  for(int i=0;i<4;i++)fd.FamilyCreate.NewAlignment(view,planes[i].GetReference(),faceRefs[i]);
  fd.Regenerate();
  void Label(string name,int a,int b,Line line,double initial){
   var ra=new ReferenceArray();ra.Append(planes[a].GetReference());ra.Append(planes[b].GetReference());
   var dim=fd.FamilyCreate.NewDimension(view,line,ra);
   var fp=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,false);
   dim.FamilyLabel=fp;fm.Set(fp,initial);parameters.Add(name,fp);
  }
  Label(widthName,0,1,Line.CreateBound(new XYZ(x0,y0-span/4,z),new XYZ(x1,y0-span/4,z)),x1-x0);
  Label(depthName,2,3,Line.CreateBound(new XYZ(x0-span/4,y0,z),new XYZ(x0-span/4,y1,z)),y1-y0);
  fd.Regenerate();
  return new{widthName,depthName};
 }
}