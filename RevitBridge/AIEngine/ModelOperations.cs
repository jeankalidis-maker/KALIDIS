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
                foreach(var key in new[]{"levelId","typeId","hostId","systemTypeId","viewId","sheetId","firstId","secondId"}){
                    if(node[key] is JsonValue val && val.TryGetValue<string>(out var reference)){
                        if(!reference.StartsWith("$")||!aliases.TryGetValue(reference.Substring(1),out long resolved))throw new ArgumentException("Unknown reference "+reference);
                        node[key]=JsonValue.Create(resolved);
                    }
                }
                using var stepDoc=JsonDocument.Parse(node.ToJsonString());
                long id=Execute(d,stepDoc.RootElement).Value;
                ids.Add(id);
                if(S(stepDoc.RootElement,"action")!="schedule_field")createdIds.Add(id);
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
            default:throw new ArgumentException("Unsupported batch action: "+S(r,"action"));
        }
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
