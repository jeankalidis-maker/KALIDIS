import {validate} from './command-registry.mjs';
export const legacyTool={name:'legacy_read',description:'Read-only compatibility subset for old Kalidis Bridge JSON actions listar, info and inventario. Does not process command files or remote branches.',inputSchema:{type:'object',properties:{expectedPath:{type:'string',minLength:1},id:{type:'string',minLength:1,maxLength:100},acao:{type:'string',enum:['listar','info','inventario']},busca:{type:'string',minLength:1,maxLength:200},elementIds:{type:'array',items:{type:'integer',minimum:1},minItems:1,maxItems:100},limite:{type:'integer',minimum:1,maximum:1000}},required:['expectedPath','id','acao'],additionalProperties:false},annotations:{readOnlyHint:true,destructiveHint:false,idempotentHint:true,openWorldHint:false}};
export async function legacyRead(a,call){
 validate(legacyTool.inputSchema,a,'legacy_read');
 if(a.elementIds&&new Set(a.elementIds).size!==a.elementIds.length)throw Error('Duplicate IDs');
 if(a.acao==='info'&&(!a.elementIds||a.busca||a.limite!==undefined))throw Error('info requires explicit IDs only');
 if(a.acao==='inventario'&&(a.elementIds||a.busca||a.limite!==undefined))throw Error('inventario does not accept filters');
 if(a.acao==='listar'&&(a.elementIds||!a.busca))throw Error('listar requires category/name search');
 const s=await call('snapshot',{});
 if(s.path.toLowerCase()!==a.expectedPath.toLowerCase())throw Error('Document mismatch');
 let dados,elementIds=[],quantidade=0;
 if(a.acao==='listar'){
  const limit=a.limite??100;
  const r=await call('elements',{query:a.busca,limit});
  elementIds=r.elements.map(e=>e.id);quantidade=elementIds.length;
  dados={elementos:r.elements,possivelmenteTruncado:quantidade===limit,escopo:'Categoria ou nome; nao busca todos os parametros.'};
 }else if(a.acao==='info'){
  const r=await call('element_info',{ids:a.elementIds});
  elementIds=r.elements.map(e=>e.id);quantidade=elementIds.length;
  dados={elementos:r.elements,escopo:'Parametros nativos; doubles em unidades internas.'};
 }else{
  const r=await call('list_categories',{});
  quantidade=r.categories.reduce((s,c)=>s+c.instanceCount,0);
  dados={categorias:r.categories,elementosComCategoria:quantidade,escopo:'Inventario por categoria, sem exportacao de CSV legado.'};
 }
 const end=await call('snapshot',{});
 if(end.path!==s.path||end.title!==s.title)throw Error('Document changed during query');
 return {id:a.id,sucesso:true,mensagem:'Leitura concluida',quantidade,elementIds,erro:null,dados,compatibility:'partial-read-only'};
}
