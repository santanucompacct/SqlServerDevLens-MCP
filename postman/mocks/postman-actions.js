// Synthetic mutable Postman API + application fixture. Never contacts a real service.
const http = require('http');
let collection, writes, runs, sequence, rejectNextWrite;
function reset() {
  writes=0; runs=0; sequence=0; rejectNextWrite=0;
  collection={info:{_postman_id:'actions',uid:'123-actions',name:'Action Fixture'},
    variable:[{key:'baseUrl',value:'http://127.0.0.1:4917/actions/application/'},{key:'azure_api_key',value:'SENSITIVE-APP-KEY'}],
    auth:{type:'apikey',apikey:[{key:'in',value:'header'},{key:'key',value:'x-functions-key'},{key:'value',value:'{{azure_api_key}}'}]},
    event:[], item:[{id:'folder',name:'Tests',item:[{id:'template',name:'Template',event:[],request:{method:'POST',url:{raw:'{{baseUrl}}report'},
      header:[{key:'spc',value:'SAMPLE'}],body:{mode:'raw',raw:'{"value":1}',options:{raw:{language:'json'}}}}}]}]};
}
reset();
function all(items=collection.item) { return items.flatMap(x=>[x,...(x.item?all(x.item):[])]); }
function native(item) { const r=item.request; return {id:item.id,name:item.name,description:null,method:r.method,url:r.url.raw,
  headerData:r.header.map(h=>({key:h.key,value:h.value,enabled:!h.disabled})),queryParams:[],dataMode:r.body?.mode??null,rawModeData:r.body?.raw??null,
  dataOptions:r.body?.options??null,auth:r.auth??null,events:item.event??[],protocolProfileBehavior:null,pathVariableData:[],variables:null,dataDisabled:false}; }
http.createServer(async (req,res)=>{
  const url=new URL(req.url,'http://fixture');
  const reply=(status,body)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(body));};
  if(url.pathname.startsWith('/application/')) {
    runs++;
    if(req.headers['x-api-key']) return reply(500,{error:'Postman control key must never reach application'});
    if(req.headers['x-functions-key']!=='SENSITIVE-APP-KEY') return reply(401,{error:'Unauthorized'});
    if(url.pathname==='/application/redirect') {res.writeHead(302,{Location:'http://127.0.0.1:4917/actions/application/side-effect'});return res.end();}
    if(url.pathname==='/application/large') return reply(200,{data:'x'.repeat(200000)});
    if(url.pathname==='/application/unauthorized') return reply(401,{error:'Denied',token:'SENSITIVE-APP-KEY'});
    const chunks=[]; for await(const chunk of req) chunks.push(chunk);
    return reply(200,{status_code:200,data:[{value:1}],token:'SENSITIVE-APP-KEY',echoed_auth:req.headers['x-functions-key'],request:JSON.parse(Buffer.concat(chunks).toString()||'{}')});
  }
  if(req.headers['x-api-key']!=='fixture-action-key') return reply(401,{error:'denied'});
  let body=null;
  if(req.method==='POST'||req.method==='PUT') {const chunks=[];for await(const chunk of req) chunks.push(chunk);body=JSON.parse(Buffer.concat(chunks).toString()||'{}');}
  if(url.pathname==='/test/reset') {reset();return reply(200,{ok:true});}
  if(url.pathname==='/test/stats') return reply(200,{writes,runs});
  if(url.pathname==='/test/change') {
    if(body.rejectNextWrite) rejectNextWrite=body.rejectNextWrite;
    if(body.script) all().find(x=>x.id==='template').event=[{listen:'prerequest',script:{exec:['throw new Error("must not run")']}}];
    if(body.route) all().find(x=>x.id==='template').request.url.raw='{{baseUrl}}'+body.route;
    if(Object.prototype.hasOwnProperty.call(body,'variable')) collection.variable[1].value=body.variable;
    collection.info.revision=(collection.info.revision??0)+1;return reply(200,{ok:true});
  }
  if(req.method==='GET'&&url.pathname==='/workspaces/workspace-actions') return reply(200,{workspace:{id:'workspace-actions',name:'Fixture',collections:[{id:'actions',uid:'123-actions',name:'Action Fixture'}]}});
  if(req.method==='GET'&&url.pathname==='/collections/123-actions') return reply(200,{collection});
  const requestMatch=url.pathname.match(/^\/collections\/actions\/requests\/([^/]+)$/);
  if(req.method==='GET'&&requestMatch) {const item=all().find(x=>x.id===requestMatch[1]);return item?.request?reply(200,{data:native(item)}):reply(404,{error:'missing'});}
  if(req.method==='POST'&&url.pathname==='/collections/actions/folders') {
    if(rejectNextWrite) {const status=rejectNextWrite;rejectNextWrite=0;return reply(status,{error:'SECRET-SERVER-BODY-NEVER-DISPLAY'});}
    const item={id:'new-folder-'+(++sequence),name:body.name,item:[]};
    (body.folder?all().find(x=>x.id===body.folder).item:collection.item).push(item);writes++;return reply(200,{data:{id:item.id}});
  }
  if((req.method==='POST'&&url.pathname==='/collections/actions/requests')||(req.method==='PUT'&&requestMatch)) {
    if(rejectNextWrite) {const status=rejectNextWrite;rejectNextWrite=0;return reply(status,{error:'SECRET-SERVER-BODY-NEVER-DISPLAY'});}
    const item=req.method==='PUT'?all().find(x=>x.id===requestMatch[1]):{id:'new-request-'+(++sequence)};
    item.name=body.name; item.event=body.events??[];
    item.request={method:body.method,url:{raw:body.url},header:(body.headerData??[]).map(h=>({key:h.key,value:h.value,disabled:h.enabled===false})),
      body:{mode:body.dataMode,raw:body.rawModeData,options:body.dataOptions}};
    if(body.auth) item.request.auth=body.auth;
    if(req.method==='POST') (url.searchParams.get('folder')?all().find(x=>x.id===url.searchParams.get('folder')).item:collection.item).push(item);
    writes++;return reply(200,{data:native(item)});
  }
  return reply(501,{error:'Fixture route not modeled'});
}).listen(process.env.PORT,'127.0.0.1');
