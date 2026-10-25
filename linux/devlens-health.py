#!/usr/bin/env python3
"""Read-only health of every configured instance through its running MCP runtime."""
import concurrent.futures, json, pathlib, re, socket, subprocess, sys

def health(directory):
    name=directory.name
    try:
        for unit in (f'devlens@{name}', f'devlens-tunnel@{name}'):
            subprocess.run(['systemctl','is-active','--quiet',unit],check=True,timeout=5)
        pid=subprocess.run(['systemctl','show','--property=MainPID','--value',f'devlens-tunnel@{name}'],
                           check=True,timeout=5,capture_output=True,text=True).stdout.strip()
        if not pid.isdigit() or int(pid)<=1: raise ValueError('tunnel pid')
        config=json.loads((directory/'instance.json').read_text())
        port=(directory/'dashboard-port').read_text().strip()
        if not port.isdigit() or not 1024<=int(port)<=65535: raise ValueError('port')
        with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as client:
            client.settimeout(30); client.connect(f'/run/devlens-{name}/mcp.sock')
            with client.makefile('rwb',buffering=0) as channel:
                def request(ident,method,params):
                    channel.write((json.dumps(dict(jsonrpc='2.0',id=ident,method=method,params=params))+'\n').encode())
                    for _ in range(20):
                        line=channel.readline(1024*1024)
                        if not line: raise ValueError('closed')
                        value=json.loads(line)
                        if value.get('id')==ident:
                            if 'error' in value: raise ValueError('protocol')
                            return value['result']
                    raise ValueError('responses')
                request(1,'initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='devlens-health',version='1')))
                channel.write(b'{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
                status=request(2,'tools/call',dict(name='get_server_status',arguments={}))
                data=json.loads(status['content'][0]['text'])
                if data['instanceId']!=name or data['database']!=config['database'] or data['mutationsEnabled']: raise ValueError('identity')
                database=request(3,'tools/call',dict(name='get_database_info',arguments={}))
                if database.get('isError'): raise ValueError('sql')
        subprocess.run(['/opt/devlens/tunnel/tunnel-client','health','--port',port,'--pid',pid,'--require-control-plane-poll','--json'],
                       check=True,timeout=15,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        return name,True
    except Exception:
        return name,False

def main():
    root=pathlib.Path('/etc/devlens')
    directories=[p for p in root.iterdir() if p.is_dir() and not p.is_symlink() and re.fullmatch('[a-z][a-z0-9-]{0,31}',p.name)] if root.exists() else []
    if not directories:
        print('No configured DevLens instances.'); return 1
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        results=list(executor.map(health, sorted(directories)))
    for name,good in results:
        print(f'{name}: '+('OK — runtime identity, SQL metadata and tunnel polling verified' if good else 'NOT READY — check both service journals, SQL/TLS and tunnel credentials/network'))
    return 0 if all(good for _,good in results) else 1

if __name__=='__main__': sys.exit(main())
