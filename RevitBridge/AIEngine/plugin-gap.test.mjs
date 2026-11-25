import assert from 'node:assert/strict';import {loadRegistry,validate} from './command-registry.mjs';import {checkFamilyCapabilities,familySchemaForEngine} from './family-capabilities.mjs';import {reviewFamilySpec} from './family-spec-review.mjs';import {famfabReview} from './famfab-definition-review.mjs';import {readFile} from 'node:fs/promises';
const reg=await loadRegistry(process.cwd()),p='C:/Sandbox/Smoke.rvt',id='fixture-uid',suffix='0123456789abcdef0123456789abcdef';
const args={create_spot_elevation:{expectedPath:p,elementId:1,viewId:2,point:{x:100,y:100}},tag_all_in_view:{expectedPath:p,viewId:2,typeId:3,categoryId:-2000011,ids:[1]},set_level_elevation:{expectedPath:p,id:1,uniqueId:id,expectedMm:12000,elevationMm:12500},import_parameters:{expectedPath:p,items:[{elementId:1,uniqueId:id,parameterId:-1010106,kind:'text',before:'old',after:'new'}]},close_document:{expectedPath:p,fileName:'CloseFixture-'+suffix+'.rvt'},link_file:{expectedPath:p,fileName:'LinkFixture-'+suffix+'.rvt',sha256:'a'.repeat(64)}};
for(const [n,a] of Object.entries(args)){const t=reg.all.get(n).tool;validate(t.inputSchema,a);assert.throws(()=>validate(t.inputSchema,{...a,unexpected:1}),/unknown/)}
assert.throws(()=>validate(reg.all.get('close_document').tool.inputSchema,{...args.close_document,fileName:'..\\Original.rvt'}),/pattern/);
assert.throws(()=>validate(reg.all.get('link_file').tool.inputSchema,{...args.link_file,sha256:'bad'}),/pattern/);
const t=reg.all.get('create_family_spec').tool,d=JSON.parse(await readFile('Integrations/FamFab/example-cabinet.json','utf8'));
const a=famfabReview({expectedPath:p,template:'test.rft',fileName:'Cabinet151.rfa',definition:d,acceptStaticGeometry:true}).spec;
validate(t.inputSchema,a);reviewFamilySpec(a);
assert.throws(()=>checkFamilyCapabilities(a,{groupedFamilyCuts:true}),/newer loaded/);
checkFamilyCapabilities(a,{groupedFamilyCuts:true,familyMultiAxis:true});
const old=familySchemaForEngine(t,{groupedFamilyCuts:true});assert(!old.inputSchema.properties.shapes.items.properties.axis);assert(!old.inputSchema.properties.expectedCategory);assert(!old.inputSchema.properties.parameters.items.properties.kind.enum.includes('yes_no'));
assert.equal(reviewFamilySpec({shapes:[{axis:'x',zMm:0,heightMm:200,radiusMm:50,centerMm:{x:100,y:100}}],parameters:[{name:'Active',kind:'yes_no',value:true},{name:'Count',kind:'integer',value:2},{name:'Angle',kind:'angle',value:30}]}).parameters,3);
assert.throws(()=>reviewFamilySpec({shapes:[{axis:'x',zMm:0,heightMm:200,radiusMm:50,radiusParameter:'R'}]}),/require z/);
assert.throws(()=>reviewFamilySpec({shapes:[{zMm:0,heightMm:200,radiusMm:50,subcategory:'missing'}]}),/Undeclared/);
console.log('PLUGIN_GAP_SCHEMA_TESTS=PASS; 6 commands and family capability gate');