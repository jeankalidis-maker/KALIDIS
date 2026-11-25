using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class AdvancedOperations
{
    internal static readonly string[] Actions={"types","native_status","library","create_family","create_family_spec","load_family","family_info","export_view","save_test","sheet","schedule"};
    private static string S(JsonElement r,string k)=>r.GetProperty(k).GetString()??throw new ArgumentException(k);
    private static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
    private static double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
    private static string FileIn(string root,string name,string ext)
    {
        if(name!=Path.GetFileName(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!name.EndsWith(ext,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid file name");
        return Path.Combine(root,name);
    }
    private static string[] Roots()=>new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Autodesk","RVT 2025","Libraries"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Autodesk","RVT 2025","Family Templates")};
    internal static object Read(UIApplication app,Document d,JsonElement r,string action)
    {
        if(action=="native_status"){
            var names=Enum.GetValues<PostableCommand>();
            string query=r.TryGetProperty("query",out var q)?q.GetString()??"":"";
            bool onlyAvailable=r.TryGetProperty("availableOnly",out var flag)&&flag.ValueKind==JsonValueKind.True;
            var allowed=new[]{"TileViews","ThinLines","ProjectBrowser","TogglePropertiesPalette","SystemBrowser","ToggleRevealHiddenElementsMode"};
            var commands=names.Where(x=>x.ToString().Contains(query,StringComparison.OrdinalIgnoreCase)).Select(x=>{
                bool available=false;try{available=app.CanPostCommand(RevitCommandId.LookupPostableCommandId(x));}catch{}
                return new{name=x.ToString(),available,approvedForPosting=allowed.Contains(x.ToString(),StringComparer.Ordinal),requiresContext=true};
            }).Where(x=>!onlyAvailable||x.available).ToArray();
            return new{ok=true,totalNative=names.Length,matched=commands.Length,commands,note="Available means Revit can accept a UI command now, not that autonomous model creation is supported."};
        }
        if(action=="types")return new {ok=true,types=new FilteredElementCollector(d).WhereElementIsElementType().Cast<ElementType>().Take(10000).Select(x=>new{id=x.Id.Value,name=x.Name,category=x.Category?.Name,kind=x.GetType().Name}).ToArray()};
        if(action=="family_info"){
            var f=d.GetElement(Id(r,"familyId")) as Family??throw new ArgumentException("Invalid family");
            return new{ok=true,name=f.Name,editable=f.IsEditable,inPlace=f.IsInPlace,types=f.GetFamilySymbolIds().Select(i=>new{id=i.Value,name=d.GetElement(i)?.Name}).ToArray()};
        }
        if(action=="library"){
            string q=S(r,"query");var files=Roots().Where(Directory.Exists).SelectMany(p=>Directory.EnumerateFiles(p,"*.rf?",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true})).Where(p=>Path.GetFileName(p).Contains(q,StringComparison.OrdinalIgnoreCase)).Take(200).ToArray();
            return new{ok=true,files};
        }
        throw new ArgumentException(action);
    }
    internal static object Write(UIApplication app,Document d,JsonElement r,string action,string root)
    {
        if(action=="create_family_spec"){
            string template=Path.GetFullPath(S(r,"template"));
            if(!Roots().Skip(1).Any(p=>template.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||!template.EndsWith(".rft",StringComparison.OrdinalIgnoreCase)||!File.Exists(template))throw new ArgumentException("Use installed RFT template");
            string output=FileIn(root,S(r,"fileName"),".rfa");if(File.Exists(output))throw new IOException("No overwrite");
            var shapes=r.GetProperty("shapes").EnumerateArray().ToArray();
            if(shapes.Length>100)throw new ArgumentException("Maximum 100 extrusions");
            int sweepCount=r.TryGetProperty("sweeps",out var swCount)?swCount.GetArrayLength():0;
            int blendCount=r.TryGetProperty("blends",out var blCount)?blCount.GetArrayLength():0;
            if(shapes.Length+sweepCount+blendCount<1)throw new ArgumentException("At least one form required");
            double dimension(JsonElement o,string k,double min,double max){double v=o.GetProperty(k).GetDouble();if(!double.IsFinite(v)||v<min||v>max)throw new ArgumentException("Invalid "+k);return Mm(v);}
            var fd=app.Application.NewFamilyDocument(template);
            try{
                using(var tx=new Transaction(fd,"Kalidis - family specification")){
                    if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction failed");
                    var fm=fd.FamilyManager;if(fm.CurrentType==null)fm.NewType("Default");
                    var parameters=new Dictionary<string,FamilyParameter>(StringComparer.OrdinalIgnoreCase);
                    var extrusions=new List<Extrusion>();
                    var allForms=new List<GenericForm>();
                    foreach(var shape in shapes){
                        var z=dimension(shape,"zMm",-30000,30000);
                        var h=dimension(shape,"heightMm",1,30000);
                        bool circular=shape.TryGetProperty("radiusMm",out _);
                        var points=circular?new[]{new XYZ(0,0,z),new XYZ(1,0,z),new XYZ(0,1,z)}:shape.GetProperty("points").EnumerateArray().Select(p=>new XYZ(dimension(p,"x",-30000,30000),dimension(p,"y",-30000,30000),z)).ToArray();
                        if(points.Length<3||points.Length>100)throw new ArgumentException("3-100 vertices");
                        var relative=shape.TryGetProperty("baseParameter",out _);
                        if(relative&&shape.TryGetProperty("heightParameter",out _))throw new ArgumentException("Use endParameter with baseParameter; height is end minus base");
                        if(relative&&((shape.TryGetProperty("void",out var relativeVoid)&&relativeVoid.ValueKind==JsonValueKind.True)||z<0))throw new ArgumentException("Relative vertical offsets require a solid at nonnegative base");
                        if(relative)points=points.Select(p=>new XYZ(p.X,p.Y,0)).ToArray();
                        var plane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,relative?0:z)));
                        var ca=new CurveArray();
                        if(circular){
                            double rad=dimension(shape,"radiusMm",10,10000);var center=new XYZ(0,0,z);if(shape.TryGetProperty("radiusParameter",out _)){ca.Append(Arc.Create(center,rad,0,Math.PI,XYZ.BasisX,XYZ.BasisY));ca.Append(Arc.Create(center,rad,Math.PI,Math.PI*2,XYZ.BasisX,XYZ.BasisY));}else ca.Append(Arc.Create(center,rad,0,Math.PI*2,XYZ.BasisX,XYZ.BasisY));
                            if(relative||shape.TryGetProperty("widthParameter",out _)||shape.TryGetProperty("depthParameter",out _))throw new ArgumentException("Circular profile does not support rectangular constraints or offset");
                        }else for(int i=0;i<points.Length;i++)ca.Append(Line.CreateBound(points[i],points[(i+1)%points.Length]));
                        var profile=new CurveArrArray();profile.Append(ca);
                        bool solid=!shape.TryGetProperty("void",out var hole)||hole.ValueKind!=JsonValueKind.True;
                        var extrusion=fd.FamilyCreate.NewExtrusion(solid,profile,plane,h);
                        extrusions.Add(extrusion);allForms.Add(extrusion);
                        if(circular&&shape.TryGetProperty("radiusParameter",out var rp)){
                            string name=rp.GetString()??throw new ArgumentException("Radius label");
                            if(parameters.ContainsKey(name)||!solid)throw new ArgumentException("Unique solid radius parameter required");
                            fd.Regenerate();
                            var plan=new FilteredElementCollector(fd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v=>!v.IsTemplate&&v.ViewType==ViewType.FloorPlan);
                            var curves=extrusion.Sketch.GetAllElements().Select(id=>fd.GetElement(id)).OfType<ModelCurve>().Where(c=>c.GeometryCurve is Arc).ToArray();
                            double radius=dimension(shape,"radiusMm",10,10000);
                            if(curves.Length!=2)throw new InvalidOperationException("Two native sketch semicircles required for driving radius");
                            var fp=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,false);fm.Set(fp,radius);parameters.Add(name,fp);
                            foreach(var curve in curves){
                                var arc=(Arc)curve.GeometryCurve;
                                var reference=arc.Reference??new Reference(curve);
                                var dim=fd.FamilyCreate.NewRadialDimension(plan,reference,arc.Center+(arc.Evaluate(.5,true)-arc.Center)*1.5);
                                dim.FamilyLabel=fp;
                            }
                            fd.Regenerate();
                            double ActualRadius(){
                                var faces=extrusion.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}).OfType<Solid>().Where(x=>x.Volume>1e-9).SelectMany(x=>x.Faces.Cast<Face>()).OfType<CylindricalFace>().ToArray();
                                if(faces.Length==0)throw new InvalidOperationException("Missing cylindrical face");
                                return faces[0].get_Radius(0).GetLength();
                            }
                            // Check the driver in the actual family document before exporting.
                            fm.Set(fp,radius*1.1);fd.Regenerate();
                            if(Math.Abs(ActualRadius()-radius*1.1)>1e-5)throw new InvalidOperationException("Radius does not drive geometry");
                            fm.Set(fp,radius);fd.Regenerate();
                            if(Math.Abs(ActualRadius()-radius)>1e-5)throw new InvalidOperationException("Radius restoration failed");
                        }
                        if(relative){
                            var start=extrusion.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM);var end=extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                            if(start==null||end==null||!end.Set(z+h)||!start.Set(z))throw new InvalidOperationException("Cannot establish extrusion offsets");
                            void BindOffset(string key,Parameter ep,double value){
                                string name=shape.GetProperty(key).GetString()??throw new ArgumentException(key);
                                if(name.Length<1||name.Length>80||parameters.ContainsKey(name))throw new ArgumentException("Unique offset parameter required");
                                var fp=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,false);fm.Set(fp,value);parameters.Add(name,fp);
                                if(!fm.CanElementParameterBeAssociated(ep))throw new InvalidOperationException("Cannot associate extrusion offset");fm.AssociateElementParameterToFamilyParameter(ep,fp);
                            }
                            BindOffset("baseParameter",start,z);BindOffset("endParameter",end,z+h);fd.Regenerate();
                        }
                        if(shape.TryGetProperty("widthParameter",out var wp)||shape.TryGetProperty("depthParameter",out _)){
                            var widthName=shape.GetProperty("widthParameter").GetString()??throw new ArgumentException("Width parameter required");
                            var depthName=shape.GetProperty("depthParameter").GetString()??throw new ArgumentException("Depth parameter required");
                            FamilyProfileConstraints.Bind(fd,extrusion,points,parameters,widthName,depthName,shape.TryGetProperty("xParameter",out var xp)?xp.GetString():null,shape.TryGetProperty("yParameter",out var yp)?yp.GetString():null);
                        }
                        if(shape.TryGetProperty("heightParameter",out var label)){
                            string name=label.GetString()??throw new ArgumentException("Invalid parameter");
                            if(name.Length<1||name.Length>80)throw new ArgumentException("Invalid parameter length");
                            if(!parameters.TryGetValue(name,out var fp)){
                                fp=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,false);
                                parameters.Add(name,fp);fm.Set(fp,h);
                            }else if(Math.Abs(fm.CurrentType.AsDouble(fp).GetValueOrDefault()-h)>1e-8)throw new ArgumentException("Shared height parameters require identical initial heights");
                            var ep=extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                            if(ep==null||!fm.CanElementParameterBeAssociated(ep))throw new InvalidOperationException("Cannot associate height");
                            fm.AssociateElementParameterToFamilyParameter(ep,fp);
                        }
                    }
                    if(r.TryGetProperty("voidCuts",out var cuts)){
                        if(cuts.GetArrayLength()>50)throw new ArgumentException("Maximum 50 explicit cuts");
                        var used=new HashSet<int>();
                        foreach(var cut in cuts.EnumerateArray()){
                            int target=cut.GetProperty("solidIndex").GetInt32(),voidIndex=cut.GetProperty("voidIndex").GetInt32();
                            if(target<0||target>=extrusions.Count||voidIndex<0||voidIndex>=extrusions.Count||target==voidIndex||!used.Add(target)||!used.Add(voidIndex))throw new ArgumentException("Each cut requires distinct unused extrusion indices");
                            var solid=extrusions[target];var cutter=extrusions[voidIndex];
                            if(!solid.IsSolid||cutter.IsSolid)throw new ArgumentException("Cut requires solid and void");
                            fd.Regenerate();
                            var members=new CombinableElementArray();members.Append(solid);members.Append(cutter);
                            var combined=fd.CombineElements(members);
                            if(combined==null)throw new InvalidOperationException("Void combination failed");
                            fd.Regenerate();
                        }
                    }
                    // Adapted from FamFab FamilyGenerator (MIT); see THIRD-PARTY-FamFab.txt.
                    if(r.TryGetProperty("blends",out var blendSpec)){
                        var blends=blendSpec.EnumerateArray().ToArray();
                        if(blends.Length>50)throw new ArgumentException("Maximum 50 blends");
                        foreach(var spec in blends){
                            double bottom=dimension(spec,"bottomMm",-30000,30000),top=dimension(spec,"topMm",-30000,30000);
                            if(top-bottom<Mm(1))throw new ArgumentException("Blend top must exceed bottom");
                            CurveArray curves(string key,double elevation){
                                var pts=spec.GetProperty(key).EnumerateArray().Select(p=>new XYZ(dimension(p,"x",-30000,30000),dimension(p,"y",-30000,30000),elevation)).ToArray();
                                if(pts.Length<3||pts.Length>100)throw new ArgumentException("3-100 blend vertices");
                                var ca=new CurveArray();for(int i=0;i<pts.Length;i++)ca.Append(Line.CreateBound(pts[i],pts[(i+1)%pts.Length]));return ca;
                            }
                            var baseCurves=curves("bottomPoints",bottom);var topCurves=curves("topPoints",top);
                            if(baseCurves.Size!=topCurves.Size)throw new ArgumentException("Blend vertex counts must match");
                            var plane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,bottom)));
                            bool solid=!spec.TryGetProperty("void",out var hole)||hole.ValueKind!=JsonValueKind.True;
                            var blend=fd.FamilyCreate.NewBlend(solid,topCurves,baseCurves,plane);
                            if(blend==null)throw new InvalidOperationException("Blend creation failed");allForms.Add(blend);
                            fd.Regenerate();
                        }
                    }
                    if(r.TryGetProperty("sweeps",out var sweepSpec)){
                        var sweeps=sweepSpec.EnumerateArray().ToArray();
                        if(sweeps.Length>50)throw new ArgumentException("Maximum 50 sweeps");
                        foreach(var spec in sweeps){
                            double z=dimension(spec,"zMm",-30000,30000);
                            XYZ point(JsonElement p)=>new XYZ(dimension(p,"x",-30000,30000),dimension(p,"y",-30000,30000),z);
                            var path=new CurveArray();
                            var segments=spec.GetProperty("path").EnumerateArray().ToArray();
                            if(segments.Length<1||segments.Length>100)throw new ArgumentException("1-100 path segments");
                            XYZ? last=null;
                            foreach(var seg in segments){
                                var start=point(seg.GetProperty("start"));var end=point(seg.GetProperty("end"));
                                if(last!=null&&last.DistanceTo(start)>Mm(0.01))throw new ArgumentException("Sweep path must be continuous");
                                Curve curve=S(seg,"kind") switch {
                                    "line"=>Line.CreateBound(start,end),
                                    "arc"=>Arc.Create(start,end,point(seg.GetProperty("mid"))),
                                    _=>throw new ArgumentException("Unknown path segment")
                                };
                                path.Append(curve);last=end;
                            }
                            var profile=new CurveArray();
                            if(spec.TryGetProperty("radiusMm",out _)){
                                double radius=dimension(spec,"radiusMm",1,5000);
                                profile.Append(Arc.Create(XYZ.Zero,radius,0,Math.PI,XYZ.BasisX,XYZ.BasisY));
                                profile.Append(Arc.Create(XYZ.Zero,radius,Math.PI,2*Math.PI,XYZ.BasisX,XYZ.BasisY));
                            }else{
                                double w=dimension(spec,"widthMm",2,10000)/2,h=dimension(spec,"heightMm",2,10000)/2;
                                var pts=new[]{new XYZ(-w,-h,0),new XYZ(w,-h,0),new XYZ(w,h,0),new XYZ(-w,h,0)};
                                for(int i=0;i<4;i++)profile.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));
                            }
                            var loops=new CurveArrArray();loops.Append(profile);
                            var sweepProfile=fd.Application.Create.NewCurveLoopsProfile(loops);
                            var pathPlane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,z)));
                            bool solid=!spec.TryGetProperty("void",out var hole)||hole.ValueKind!=JsonValueKind.True;
                            var sweep=fd.FamilyCreate.NewSweep(solid,path,pathPlane,sweepProfile,0,ProfilePlaneLocation.Start);
                            if(sweep==null)throw new InvalidOperationException("Sweep failed");allForms.Add(sweep);
                            fd.Regenerate();
                        }
                    }
                    if(r.TryGetProperty("formCuts",out var formCuts)){
                        if(formCuts.GetArrayLength()>50)throw new ArgumentException("Maximum 50 cut groups");
                        if(formCuts.GetArrayLength()>0&&r.TryGetProperty("voidCuts",out var legacy)&&legacy.GetArrayLength()>0)throw new ArgumentException("Choose formCuts or voidCuts");
                        var used=new HashSet<int>();
                        foreach(var cut in formCuts.EnumerateArray()){
                            int index=cut.GetProperty("solidIndex").GetInt32();
                            if(index<0||index>=allForms.Count||!used.Add(index)||!allForms[index].IsSolid)throw new ArgumentException("Invalid target form");
                            var cutters=cut.GetProperty("voidIndices").EnumerateArray().Select(e=>e.GetInt32()).ToArray();
                            if(cutters.Length<1||cutters.Length>10)throw new ArgumentException("1-10 cutters required");
                            var members=new CombinableElementArray();members.Append(allForms[index]);
                            foreach(var i in cutters){
                                if(i<0||i>=allForms.Count||!used.Add(i)||allForms[i].IsSolid)throw new ArgumentException("Invalid or reused cutter form");
                                members.Append(allForms[i]);
                            }
                            fd.Regenerate();
                            if(fd.CombineElements(members)==null)throw new InvalidOperationException("Form combination failed");
                            fd.Regenerate();
                        }
                    }
                    if(r.TryGetProperty("referencePlanes",out var planeSpec)){
                        var planes=planeSpec.EnumerateArray().ToArray();
                        if(planes.Length>50)throw new ArgumentException("Maximum 50 reference planes");
                        var view=new FilteredElementCollector(fd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v=>!v.IsTemplate)??throw new InvalidOperationException("Family plan view required");
                        var existing=new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().Select(p=>p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach(var spec in planes){
                            var name=S(spec,"name");if(!existing.Add(name))throw new ArgumentException("Duplicate reference plane "+name);
                            double offset=dimension(spec,"offsetMm",-30000,30000),span=Mm(2000);
                            var axis=S(spec,"axis");
                            // In a plan view, vertical reference planes are represented by XY lines.
                            XYZ a,b;
                            if(axis=="x"){a=new XYZ(offset,-span,0);b=new XYZ(offset,span,0);}
                            else if(axis=="y"){a=new XYZ(-span,offset,0);b=new XYZ(span,offset,0);}
                            else throw new ArgumentException("Supported reference axes x or y");
                            var plane=fd.FamilyCreate.NewReferencePlane(a,b,XYZ.BasisZ,view);plane.Name=name;
                        }
                    }
                    if(r.TryGetProperty("parameters",out var paramSpec)){
                        var defs=paramSpec.EnumerateArray().ToArray();
                        if(defs.Length>100)throw new ArgumentException("Maximum 100 additional parameters");
                        foreach(var def in defs){
                            var name=S(def,"name");if(parameters.ContainsKey(name))throw new ArgumentException("Duplicate parameter");
                            var kind=S(def,"kind");
                            var spec=kind switch{"length"=>SpecTypeId.Length,"number"=>SpecTypeId.Number,"text"=>SpecTypeId.String.Text,_=>throw new ArgumentException("Unsupported parameter kind")};
                            bool instance=def.TryGetProperty("instance",out var inst)&&inst.ValueKind==JsonValueKind.True;
                            var fp=fm.AddParameter(name,GroupTypeId.Data,spec,instance);parameters.Add(name,fp);
                            if(kind=="text")fm.Set(fp,S(def,"value"));else if(kind=="length")fm.Set(fp,dimension(def,"value",-30000,30000));else fm.Set(fp,def.GetProperty("value").GetDouble());
                        }
                    }
                    if(r.TryGetProperty("formulas",out var formulas))foreach(var f in formulas.EnumerateObject()){
                        if(!parameters.TryGetValue(f.Name,out var fp))throw new ArgumentException("Unknown formula parameter");
                        fm.SetFormula(fp,f.Value.GetString());
                    }
                    fd.Regenerate();
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Family transaction failed");
                }
                fd.SaveAs(output,new SaveAsOptions{OverwriteExistingFile=false});
                return new{ok=true,path=output,extrusionCount=shapes.Length,sweepCount,blendCount};
            }finally{fd.Close(false);}
        }
        if(action=="create_family"){
            string template=Path.GetFullPath(S(r,"template"));
            if(!Roots().Skip(1).Any(p=>template.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||!template.EndsWith(".rft",StringComparison.OrdinalIgnoreCase)||!File.Exists(template))throw new ArgumentException("Use installed RFT template");
            string output=FileIn(root,S(r,"fileName"),".rfa");if(File.Exists(output))throw new IOException("No overwrite");
            double w=r.GetProperty("widthMm").GetDouble(),l=r.GetProperty("lengthMm").GetDouble(),h=r.GetProperty("heightMm").GetDouble();
            if(new[]{w,l,h}.Any(n=>!double.IsFinite(n)||n<10||n>30000))throw new ArgumentException("Invalid dimensions");
            var fd=app.Application.NewFamilyDocument(template);
            try{
                using(var tx=new Transaction(fd,"Kalidis - family solid")){
                    tx.Start();
                    var plane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
                    var points=new[]{XYZ.Zero,new XYZ(Mm(w),0,0),new XYZ(Mm(w),Mm(l),0),new XYZ(0,Mm(l),0)};
                    var ca=new CurveArray();for(int i=0;i<4;i++)ca.Append(Line.CreateBound(points[i],points[(i+1)%4]));
                    var profile=new CurveArrArray();profile.Append(ca);
                    var solid=fd.FamilyCreate.NewExtrusion(true,profile,plane,Mm(h));
                    var fm=fd.FamilyManager;if(fm.CurrentType==null)fm.NewType("Default");
                    var height=fm.AddParameter("Kalidis_Height",GroupTypeId.Geometry,SpecTypeId.Length,false);
                    var ep=solid.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                    if(ep==null||!fm.CanElementParameterBeAssociated(ep))throw new InvalidOperationException("Height association unsupported");
                    fm.AssociateElementParameterToFamilyParameter(ep,height);fm.Set(height,Mm(h));
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Family transaction failed");
                }
                fd.SaveAs(output,new SaveAsOptions{OverwriteExistingFile=false});return new{ok=true,path=output,parametric="height",widthMm=w,lengthMm=l,heightMm=h};
            }finally{fd.Close(false);}
        }
        if(action=="export_view"){
            string output=FileIn(root,S(r,"fileName"),".png");if(File.Exists(output))throw new IOException("No overwrite");
            var opts=new ImageExportOptions{ExportRange=ExportRange.CurrentView,FilePath=output,HLRandWFViewsFileType=ImageFileType.PNG,ShadowViewsFileType=ImageFileType.PNG,ZoomType=ZoomFitType.FitToPage,PixelSize=1600};
            d.ExportImage(opts);return new{ok=File.Exists(output),path=output};
        }
        if(action=="save_test"){d.Save();return new{ok=true,path=d.PathName};}
        using var t=new Transaction(d,"Kalidis - "+action);t.Start();
        try{
            ElementId id;
            if(action=="load_family"){
                string p=Path.GetFullPath(S(r,"path"));
                if(!Roots().Take(1).Append(root.TrimEnd(Path.DirectorySeparatorChar)).Any(x=>p.StartsWith(x+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||!p.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||!File.Exists(p))throw new ArgumentException("Family must be staged locally");
                if(!d.LoadFamily(p,out Family f)||f==null)throw new InvalidOperationException("Load failed or already loaded");id=f.Id;
            }else if(action=="sheet"){
                var v=ViewSheet.Create(d,Id(r,"titleBlockId"));v.SheetNumber=S(r,"number");v.Name=S(r,"name");id=v.Id;
            }else if(action=="schedule"){
                var v=ViewSchedule.CreateSchedule(d,Id(r,"categoryId"));v.Name=S(r,"name");id=v.Id;
            }else throw new ArgumentException(action);
            bool commit=r.TryGetProperty("commit",out var c)&&c.ValueKind==JsonValueKind.True;
            var state=commit?t.Commit():t.RollBack();
            return new{ok=state==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack),status=state.ToString(),id=id.Value,dryRun=!commit};
        }catch{if(t.GetStatus()==TransactionStatus.Started)t.RollBack();throw;}
    }
}
