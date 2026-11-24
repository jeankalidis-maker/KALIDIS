using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;

namespace Kalidis.AIEngine;
internal static class ModelOperations
{
    private static double Mm(double x) => UnitUtils.ConvertToInternalUnits(x, UnitTypeId.Millimeters);
    private static double N(JsonElement r,string k) {
        double n=r.GetProperty(k).GetDouble();
        if(!double.IsFinite(n)||Math.Abs(n)>10000000) throw new ArgumentException("Invalid dimension: "+k);
        return n;
    }
    private static string S(JsonElement r,string k)=>r.GetProperty(k).GetString() ?? throw new ArgumentException(k);
    private static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
    private static T Get<T>(Document d,JsonElement r,string k) where T:Element =>
        d.GetElement(Id(r,k)) as T ?? throw new ArgumentException("Invalid "+k);
    private static XYZ P(JsonElement r,double z=0) => new(Mm(N(r,"x")),Mm(N(r,"y")),z);
    private static XYZ P3(JsonElement r) => new(Mm(N(r,"x")),Mm(N(r,"y")),Mm(N(r,"z")));
    private static CurveLoop Profile(JsonElement r,double z)
    {
        var p=r.GetProperty("points").EnumerateArray().Select(x=>P(x,z)).ToArray();
        if(p.Length<3 || p.Length>200) throw new ArgumentException("Profile needs 3 to 200 vertices");
        var loop=new CurveLoop();
        for(int i=0;i<p.Length;i++) loop.Append(Line.CreateBound(p[i],p[(i+1)%p.Length]));
        return loop;
    }
    public static object Batch(Document d,JsonElement r)
    {
        var timer=Stopwatch.StartNew();
        var steps=r.GetProperty("steps").EnumerateArray().ToArray();
        if(steps.Length<1 || steps.Length>200) throw new ArgumentException("Batch limit 1-200");
        bool commit=r.TryGetProperty("commit",out var flag)&&flag.ValueKind==JsonValueKind.True;
        var ids=new List<long>();
        using var tx=new Transaction(d,"Kalidis AI Engine - batch");
        if(tx.Start()!=TransactionStatus.Started) throw new InvalidOperationException("Transaction not started");
        try {
            var createdIds=new List<long>();
            var aliases=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var step in steps){
                var node=JsonNode.Parse(step.GetRawText())!.AsObject();
                string? alias=node["alias"]?.GetValue<string>();
                node.Remove("alias");
                if(alias!=null&&(alias.Length==0||alias.Length>64||aliases.ContainsKey(alias)))throw new ArgumentException("Invalid or duplicate alias");
                foreach(var key in new[]{"levelId","typeId","hostId","systemTypeId","viewId","sheetId","firstId","secondId","thirdId","templateId"}){
                    if(node[key] is JsonValue val && val.TryGetValue<string>(out var reference)){
                        if(!reference.StartsWith("$")||!aliases.TryGetValue(reference.Substring(1),out long resolved))throw new ArgumentException("Unknown reference "+reference);
                        node[key]=JsonValue.Create(resolved);
                    }
                }
                using var stepDoc=JsonDocument.Parse(node.ToJsonString());
                long id=Execute(d,stepDoc.RootElement).Value;
                ids.Add(id);
                if(!new[]{"schedule_field","wall_top_constraint","join_geometry","unjoin_geometry","apply_view_template","set_view_detail_level","set_section_box","hide_elements","unhide_elements"}.Contains(S(stepDoc.RootElement,"action")))createdIds.Add(id);
                if(alias!=null)aliases.Add(alias,id);
            }
            d.Regenerate();
            var status=commit?tx.Commit():tx.RollBack();
            bool ok=commit?status==TransactionStatus.Committed:status==TransactionStatus.RolledBack;
            return new {ok,committed=ok&&commit,dryRun=!commit,status=status.ToString(),elementIds=ids,aliases,verified=createdIds.All(i=>(d.GetElement(new ElementId(i))!=null)==commit),elapsedMs=timer.ElapsedMilliseconds};
        }
        catch { if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw; }
    }
    private static ElementId Execute(Document d,JsonElement r)
    {
        switch(S(r,"action"))
        {
            case "level": { var e=Level.Create(d,Mm(N(r,"elevationMm")));e.Name=S(r,"name");return e.Id; }
            case "wall": {
                var l=Get<Level>(d,r,"levelId");var t=Get<WallType>(d,r,"typeId");
                if(t.Kind!=WallKind.Basic)throw new ArgumentException("Basic wall type required");
                double h=N(r,"heightMm");if(h<=0||h>30000)throw new ArgumentException("Invalid wall height");
                return Wall.Create(d,Line.CreateBound(P(r.GetProperty("start"),l.Elevation),P(r.GetProperty("end"),l.Elevation)),t.Id,l.Id,Mm(h),0,false,false).Id;
            }
            case "floor": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FloorType>(d,r,"typeId");
                return Floor.Create(d,new List<CurveLoop>{Profile(r,l.Elevation)},t.Id,l.Id).Id;
            }
            case "ceiling": {
                var l=Get<Level>(d,r,"levelId");var t=Get<CeilingType>(d,r,"typeId");
                return Ceiling.Create(d,new List<CurveLoop>{Profile(r,0)},t.Id,l.Id).Id;
            }
            case "room": {
                var l=Get<Level>(d,r,"levelId");var p=P(r);
                var room=d.Create.NewRoom(l,new UV(p.X,p.Y));if(room==null)throw new InvalidOperationException("Room failed");
                room.Name=S(r,"name");return room.Id;
            }
            case "grid": {var e=Grid.Create(d,Line.CreateBound(P(r.GetProperty("start")),P(r.GetProperty("end"))));e.Name=S(r,"name");return e.Id;}
            case "material": return Material.Create(d,S(r,"name"));
            case "view3d": {
                var t=Get<ViewFamilyType>(d,r,"typeId");var v=View3D.CreateIsometric(d,t.Id);v.Name=S(r,"name");return v.Id;
            }
            case "plan": {
                var t=Get<ViewFamilyType>(d,r,"typeId");var l=Get<Level>(d,r,"levelId");var v=ViewPlan.Create(d,t.Id,l.Id);v.Name=S(r,"name");return v.Id;
            }
            case "beam": {
                var l=Get<Level>(d,r,"levelId"); var t=Get<FamilySymbol>(d,r,"typeId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(Line.CreateBound(P3(r.GetProperty("start")),P3(r.GetProperty("end"))),t,l,StructuralType.Beam).Id;
            }
            case "column":
            case "component": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FamilySymbol>(d,r,"typeId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(P3(r.GetProperty("point")),t,l,S(r,"action")=="column"?StructuralType.Column:StructuralType.NonStructural).Id;
            }
            case "hosted_component": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FamilySymbol>(d,r,"typeId");var host=Get<Wall>(d,r,"hostId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(P3(r.GetProperty("point")),t,host,l,StructuralType.NonStructural).Id;
            }
            case "pipe": return Pipe.Create(d,Id(r,"systemTypeId"),Id(r,"typeId"),Id(r,"levelId"),P3(r.GetProperty("start")),P3(r.GetProperty("end"))).Id;
            case "duct": return Duct.Create(d,Id(r,"systemTypeId"),Id(r,"typeId"),Id(r,"levelId"),P3(r.GetProperty("start")),P3(r.GetProperty("end"))).Id;
            case "conduit": return Conduit.Create(d,Id(r,"typeId"),P3(r.GetProperty("start")),P3(r.GetProperty("end")),Id(r,"levelId")).Id;
            case "cable_tray": return CableTray.Create(d,Id(r,"typeId"),P3(r.GetProperty("start")),P3(r.GetProperty("end")),Id(r,"levelId")).Id;
            case "opening": {
                var wall=Get<Wall>(d,r,"hostId");return d.Create.NewOpening(wall,P3(r.GetProperty("start")),P3(r.GetProperty("end"))).Id;
            }
            case "text": {
                var v=Get<View>(d,r,"viewId");return TextNote.Create(d,v.Id,P3(r.GetProperty("point")),S(r,"text"),Id(r,"typeId")).Id;
            }
            case "detail_line": {
                var v=Get<View>(d,r,"viewId");return d.Create.NewDetailCurve(v,Line.CreateBound(P3(r.GetProperty("start")),P3(r.GetProperty("end")))).Id;
            }
            case "viewport": return Viewport.Create(d,Id(r,"sheetId"),Id(r,"viewId"),P3(r.GetProperty("point"))).Id;
            case "foundation": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FamilySymbol>(d,r,"typeId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(P3(r.GetProperty("point")),t,l,StructuralType.Footing).Id;
            }
            case "terrain": {
                var l=Get<Level>(d,r,"levelId");var t=Get<ToposolidType>(d,r,"typeId");
                var points=r.GetProperty("points").EnumerateArray().Select(P3).ToList();
                if(points.Count<3||points.Count>5000)throw new ArgumentException("3-5000 terrain points");
                return Toposolid.Create(d,points,t.Id,l.Id).Id;
            }
            case "section": {
                var t=Get<ViewFamilyType>(d,r,"typeId");
                var start=P3(r.GetProperty("start"));var end=P3(r.GetProperty("end"));
                var delta=end-start;var horizontal=new XYZ(delta.X,delta.Y,0);
                if(horizontal.GetLength()<Mm(100))throw new ArgumentException("Section too short");
                var x=horizontal.Normalize();var y=XYZ.BasisZ;var z=x.CrossProduct(y);
                var transform=Transform.Identity;transform.Origin=start;transform.BasisX=x;transform.BasisY=y;transform.BasisZ=z;
                double height=Mm(N(r,"heightMm")),depth=Mm(N(r,"depthMm"));
                if(height<=0||depth<=0)throw new ArgumentException("Positive section extents required");
                var box=new BoundingBoxXYZ{Transform=transform,Min=new XYZ(0,0,0),Max=new XYZ(horizontal.GetLength(),height,depth)};
                var v=ViewSection.CreateSection(d,t.Id,box);v.Name=S(r,"name");return v.Id;
            }
            case "roof": {
                var l=Get<Level>(d,r,"levelId");var t=Get<RoofType>(d,r,"typeId");
                var curves=new CurveArray();foreach(var c in Profile(r,l.Elevation))curves.Append(c);
                ModelCurveArray edges;var roof=d.Create.NewFootPrintRoof(curves,l,t,out edges);
                double slope=N(r,"slopeDegrees");if(slope<0||slope>=80)throw new ArgumentException("Invalid slope");
                foreach(ModelCurve edge in edges){roof.set_DefinesSlope(edge,slope>0);if(slope>0)roof.set_SlopeAngle(edge,slope*Math.PI/180);}
                return roof.Id;
            }
            case "model_line": {
                var a=P3(r.GetProperty("start"));var b=P3(r.GetProperty("end"));var direction=(b-a).Normalize();
                var normal=direction.CrossProduct(Math.Abs(direction.DotProduct(XYZ.BasisZ))>0.99?XYZ.BasisX:XYZ.BasisZ).Normalize();
                var plane=SketchPlane.Create(d,Plane.CreateByNormalAndOrigin(normal,a));
                return d.Create.NewModelCurve(Line.CreateBound(a,b),plane).Id;
            }
            case "tag": {
                var v=Get<View>(d,r,"viewId");var host=d.GetElement(Id(r,"hostId"))??throw new ArgumentException("Invalid host");
                return IndependentTag.Create(d,Id(r,"typeId"),v.Id,new Reference(host),false,TagOrientation.Horizontal,P3(r.GetProperty("point"))).Id;
            }
            case "dimension": {
                var v=Get<View>(d,r,"viewId");
                var refs=new ReferenceArray();
                foreach(var id in r.GetProperty("gridIds").EnumerateArray()){
                    var grid=d.GetElement(new ElementId(id.GetInt64())) as Grid??throw new ArgumentException("Expected grid");
                    refs.Append(new Reference(grid));
                }
                if(refs.Size<2||refs.Size>50)throw new ArgumentException("2-50 grid references");
                return d.Create.NewDimension(v,Line.CreateBound(P3(r.GetProperty("start")),P3(r.GetProperty("end"))),refs).Id;
            }
            case "schedule_field": {
                var schedule=Get<ViewSchedule>(d,r,"viewId");
                var definition=schedule.Definition;
                var field=definition.GetSchedulableFields().Single(f=>f.ParameterId==Id(r,"parameterId"));
                definition.AddField(field);return schedule.Id;
            }
            case "duplicate_type": {
                var t=Get<ElementType>(d,r,"typeId");return t.Duplicate(S(r,"name")).Id;
            }
            case "elbow":
            case "union": {
                d.Regenerate();
                var a=ConnectorAt(d,r,"firstId","firstPoint");var b=ConnectorAt(d,r,"secondId","secondPoint");
                if(a.Owner.Id==b.Owner.Id||a.IsConnected||b.IsConnected||a.Domain!=b.Domain)throw new ArgumentException("Incompatible or already connected connectors");
                return (S(r,"action")=="elbow"?d.Create.NewElbowFitting(a,b):d.Create.NewUnionFitting(a,b)).Id;
            }
            case "wall_top_constraint": {
                var wall=Get<Wall>(d,r,"hostId");var top=Get<Level>(d,r,"levelId");
                var p=wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);if(p==null||p.IsReadOnly||!p.Set(top.Id))throw new InvalidOperationException("Cannot set top constraint");
                if(r.TryGetProperty("offsetMm",out var off)){var o=wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);if(o==null||o.IsReadOnly||!o.Set(Mm(N(r,"offsetMm"))))throw new InvalidOperationException("Cannot set top offset");}
                return wall.Id;
            }
            case "join_geometry":
            case "unjoin_geometry": {
                var a=Get<Element>(d,r,"firstId");var b=Get<Element>(d,r,"secondId");
                if(a.Id==b.Id)throw new ArgumentException("Distinct elements required");
                bool joined=JoinGeometryUtils.AreElementsJoined(d,a,b);
                if(S(r,"action")=="join_geometry"&&!joined)JoinGeometryUtils.JoinGeometry(d,a,b);
                if(S(r,"action")=="unjoin_geometry"&&joined)JoinGeometryUtils.UnjoinGeometry(d,a,b);
                return a.Id;
            }
            case "room_tag": {
                var v=Get<View>(d,r,"viewId");var room=Get<Autodesk.Revit.DB.Architecture.Room>(d,r,"hostId");var p=P(r.GetProperty("point"));
                var tag=d.Create.NewRoomTag(new LinkElementId(room.Id),new UV(p.X,p.Y),v.Id);
                if(tag==null)throw new InvalidOperationException("Room tag failed");return tag.Id;
            }
            case "schedule_on_sheet": {
                var sheet=Get<ViewSheet>(d,r,"sheetId");var schedule=Get<ViewSchedule>(d,r,"viewId");
                return ScheduleSheetInstance.Create(d,sheet.Id,schedule.Id,P3(r.GetProperty("point"))).Id;
            }
            case "area_plan": {
                var scheme=Get<AreaScheme>(d,r,"typeId");var l=Get<Level>(d,r,"levelId");
                var v=ViewPlan.CreateAreaPlan(d,scheme.Id,l.Id);v.Name=S(r,"name");return v.Id;
            }
            case "area_boundary": {
                var v=Get<ViewPlan>(d,r,"viewId");var a=P3(r.GetProperty("start"));var b=P3(r.GetProperty("end"));
                if(Math.Abs(a.Z-b.Z)>1e-8)throw new ArgumentException("Horizontal area boundary required");
                var plane=SketchPlane.Create(d,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,a));
                return d.Create.NewAreaBoundaryLine(plane,Line.CreateBound(a,b),v).Id;
            }
            case "area": {
                var v=Get<ViewPlan>(d,r,"viewId");var p=P(r.GetProperty("point"));var area=d.Create.NewArea(v,new UV(p.X,p.Y));
                if(area==null)throw new InvalidOperationException("Area creation failed");area.Name=S(r,"name");return area.Id;
            }
            // Adapted from KenLP/RevitMCPServer (MIT); see THIRD-PARTY-KenLP.txt.
            case "apply_view_template": {
                var v=Get<View>(d,r,"viewId");var tid=Id(r,"templateId");
                if(tid!=ElementId.InvalidElementId&&(!v.IsValidViewTemplate(tid)))throw new ArgumentException("Incompatible view template");
                v.ViewTemplateId=tid;return v.Id;
            }
            case "set_view_detail_level": {
                var v=Get<View>(d,r,"viewId");var name=S(r,"detailLevel");
                if(!new[]{"Coarse","Medium","Fine"}.Contains(name,StringComparer.OrdinalIgnoreCase))throw new ArgumentException("Coarse, Medium or Fine required");
                v.DetailLevel=Enum.Parse<ViewDetailLevel>(name,true);return v.Id;
            }
            case "set_section_box": {
                var v=Get<View3D>(d,r,"viewId");
                if(r.TryGetProperty("enable",out var enabled)&&enabled.ValueKind==JsonValueKind.False){v.IsSectionBoxActive=false;return v.Id;}
                var a=P3(r.GetProperty("min"));var b=P3(r.GetProperty("max"));
                var lo=new XYZ(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
                var hi=new XYZ(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
                if(hi.X-lo.X<Mm(1)||hi.Y-lo.Y<Mm(1)||hi.Z-lo.Z<Mm(1))throw new ArgumentException("Section box must have positive volume");
                v.SetSectionBox(new BoundingBoxXYZ{Min=lo,Max=hi});v.IsSectionBoxActive=true;return v.Id;
            }
            case "duplicate_view": {
                var v=Get<View>(d,r,"viewId");var mode=r.TryGetProperty("duplicateOption",out var opt)?opt.GetString():"Duplicate";
                if(!Enum.TryParse<ViewDuplicateOption>(mode,true,out var option)||!Enum.IsDefined(option)||!v.CanViewBeDuplicated(option))throw new ArgumentException("Invalid view duplication mode");
                var id=v.Duplicate(option);d.GetElement(id).Name=S(r,"name");return id;
            }
            case "hide_elements":
            case "unhide_elements": {
                var v=Get<View>(d,r,"viewId");
                var ids=r.GetProperty("ids").EnumerateArray().Select(x=>new ElementId(x.GetInt64())).Distinct().ToList();
                if(ids.Count<1||ids.Count>500)throw new ArgumentException("1-500 IDs required");
                if(S(r,"action")=="hide_elements")v.HideElements(ids);else v.UnhideElements(ids);return v.Id;
            }
            case "group_elements": {
                var ids=r.GetProperty("ids").EnumerateArray().Select(x=>new ElementId(x.GetInt64())).Distinct().ToList();
                if(ids.Count<1||ids.Count>500)throw new ArgumentException("1-500 IDs required");
                var g=d.Create.NewGroup(ids);g.GroupType.Name=S(r,"name");return g.Id;
            }
            case "perspective": {
                var t=Get<ViewFamilyType>(d,r,"typeId");var eye=P3(r.GetProperty("eye"));var target=P3(r.GetProperty("target"));
                if(eye.DistanceTo(target)<Mm(1))throw new ArgumentException("Eye and target must differ");
                var forward=(target-eye).Normalize();var up=XYZ.BasisZ-forward.DotProduct(XYZ.BasisZ)*forward;
                up=up.GetLength()<1e-6?XYZ.BasisY:up.Normalize();
                var v=View3D.CreatePerspective(d,t.Id);v.SetOrientation(new ViewOrientation3D(eye,up,forward));v.Name=S(r,"name");return v.Id;
            }
            case "mirror_copy": {
                var e=Get<Element>(d,r,"hostId");if(e.Pinned)throw new ArgumentException("Pinned element");
                var normal=P3(r.GetProperty("normal"));if(normal.GetLength()<1e-9)throw new ArgumentException("Nonzero normal required");
                var ids=ElementTransformUtils.MirrorElements(d,new List<ElementId>{e.Id},Plane.CreateByNormalAndOrigin(normal.Normalize(),P3(r.GetProperty("origin"))),true);
                if(ids.Count!=1)throw new InvalidOperationException("Mirror produced multiple elements; rolled back");return ids.First();
            }
            case "pipe_sized": {
                var e=Pipe.Create(d,Id(r,"systemTypeId"),Id(r,"typeId"),Id(r,"levelId"),P3(r.GetProperty("start")),P3(r.GetProperty("end")));
                SetPositiveLength(e,BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,N(r,"diameterMm"));return e.Id;
            }
            case "duct_sized": {
                var e=Duct.Create(d,Id(r,"systemTypeId"),Id(r,"typeId"),Id(r,"levelId"),P3(r.GetProperty("start")),P3(r.GetProperty("end")));
                if(r.TryGetProperty("diameterMm",out _)){
                    if(r.TryGetProperty("widthMm",out _)||r.TryGetProperty("heightMm",out _))throw new ArgumentException("Choose round or rectangular dimensions");
                    SetPositiveLength(e,BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,N(r,"diameterMm"));
                }else{
                    SetPositiveLength(e,BuiltInParameter.RBS_CURVE_WIDTH_PARAM,N(r,"widthMm"));
                    SetPositiveLength(e,BuiltInParameter.RBS_CURVE_HEIGHT_PARAM,N(r,"heightMm"));
                }return e.Id;
            }
            case "transition": {
                d.Regenerate();var a=ConnectorAt(d,r,"firstId","firstPoint");var b=ConnectorAt(d,r,"secondId","secondPoint");
                if(a.Owner.Id==b.Owner.Id||a.IsConnected||b.IsConnected||a.Domain!=b.Domain)throw new ArgumentException("Incompatible connectors");
                return d.Create.NewTransitionFitting(a,b).Id;
            }
            case "tee": {
                d.Regenerate();var a=ConnectorAt(d,r,"firstId","firstPoint");var b=ConnectorAt(d,r,"secondId","secondPoint");var c=ConnectorAt(d,r,"thirdId","thirdPoint");
                if(new[]{a.Owner.Id,b.Owner.Id,c.Owner.Id}.Distinct().Count()!=3||a.IsConnected||b.IsConnected||c.IsConnected||a.Domain!=b.Domain||a.Domain!=c.Domain)throw new ArgumentException("Incompatible connectors");
                return d.Create.NewTeeFitting(a,b,c).Id;
            }
            default:throw new ArgumentException("Unsupported batch action: "+S(r,"action"));
        }
    }
    private static void SetPositiveLength(Element e,BuiltInParameter key,double value){
        if(value<=0||value>30000)throw new ArgumentException("Invalid dimension");
        var p=e.get_Parameter(key);if(p==null||p.IsReadOnly||!p.Set(Mm(value)))throw new InvalidOperationException("Cannot set dimension "+key);
    }
    private static Connector ConnectorAt(Document d,JsonElement r,string key,string point){
        var e=d.GetElement(Id(r,key));var manager=e is MEPCurve c?c.ConnectorManager:(e as FamilyInstance)?.MEPModel?.ConnectorManager;
        if(manager==null)throw new ArgumentException("No connector manager");
        var p=P3(r.GetProperty(point));var connectors=manager.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End).OrderBy(c=>c.Origin.DistanceTo(p)).ToArray();
        if(connectors.Length==0||connectors[0].Origin.DistanceTo(p)>Mm(20))throw new ArgumentException("No end connector within 20 mm");return connectors[0];
    }
    public static object ExportFamily(Document d,JsonElement r,string sandbox)
    {
        var f=Get<Family>(d,r,"familyId");
        if(!f.IsEditable||f.IsInPlace)throw new InvalidOperationException("Family not editable");
        string file=S(r,"fileName");
        if(file!=Path.GetFileName(file)||!file.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||file.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Use simple RFA filename");
        string output=Path.Combine(sandbox,file);
        if(File.Exists(output))throw new IOException("Refusing to overwrite RFA");
        var fd=d.EditFamily(f);
        try {fd.SaveAs(output,new SaveAsOptions{OverwriteExistingFile=false});return new {ok=true,path=output};}
        finally {fd.Close(false);}
    }
}
