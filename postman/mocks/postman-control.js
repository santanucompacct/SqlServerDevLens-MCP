// Synthetic control-API fixture based on Postman's documented GET responses.
// No user workspace, key or collection is copied here. Never logs auth headers.
const http = require('http');
http.createServer((req, res) => {
  const reply = (status, data) => { res.writeHead(status, {'Content-Type':'application/json'}); res.end(JSON.stringify(data)); };
  if (req.method !== 'GET') return reply(501, {error:'Only GET metadata routes are modeled'});
  const key = req.headers['x-api-key'];
  if (key === 'fixture-unauthorized') return reply(401, {error:'SECRET-BODY-must-not-leak'});
  if (key === 'fixture-forbidden') return reply(403, {error:'SECRET-BODY-must-not-leak'});
  if (key === 'fixture-rate-limit') return reply(429, {error:'SECRET-BODY-must-not-leak'});
  if (key === 'fixture-server-error') return reply(503, {error:'SECRET-BODY-must-not-leak'});
  if (key === 'fixture-redirect') { res.writeHead(302, {Location:'http://127.0.0.1:1/leak'}); return res.end(); }
  if (key === 'fixture-malformed') { res.writeHead(200); return res.end('not-json-SECRET-BODY'); }
  if (key === 'fixture-hang') return setTimeout(() => reply(200, {workspaces:[]}), 3000);
  if (key !== 'fixture-valid') return reply(401, {error:'invalid fixture key'});
  if (req.url === '/workspaces') return reply(200, {workspaces:[{id:'workspace-1',name:'Fixture Workspace',type:'personal'}]});
  if (req.url === '/workspaces/workspace-1') return reply(200, {workspace:{id:'workspace-1',name:'Fixture Workspace',collections:[{id:'collection-1',uid:'123-collection-1',name:'Fixture Collection'}]}});
  if (req.url === '/collections/123-collection-1') return reply(200, {collection:{
    info:{_postman_id:'collection-1',uid:'123-collection-1',name:'Fixture Collection'},
    variable:[{key:'baseUrl',value:'https://private.invalid'},{key:'token',value:'SECRET-VARIABLE'}],
    auth:{type:'bearer',bearer:[{key:'token',value:'SECRET-AUTH'}]},
    item:[{name:'Folder',item:[{name:'Read',request:{method:'GET',url:'https://private.invalid?token=SECRET-URL',header:[{key:'Authorization',value:'SECRET-HEADER'}]}}]},
      {name:'Write',request:{method:'POST',url:'https://private.invalid',body:{raw:'SECRET-BODY'}}}],
    event:[{listen:'prerequest',script:{exec:["throw new Error('SCRIPTS-MUST-NOT-RUN')"]}}]
  }});
  return reply(404, {error:'not found'});
}).listen(process.env.PORT, '127.0.0.1');
