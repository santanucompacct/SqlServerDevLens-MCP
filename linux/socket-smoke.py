"""Offline persistent runtime/stdio bridge lifecycle, using synthetic credentials only."""
import json, os, pathlib, queue, subprocess, sys, tempfile, threading, time

exe=sys.argv[1]
with tempfile.TemporaryDirectory(prefix='dvs-') as tmp:
    directory=pathlib.Path(tmp)/'tenant-a'; directory.mkdir(mode=0o700)
    config=directory/'instance.json'; config.write_text(json.dumps(dict(instanceId='tenant-a',host='localhost',port=1,database='SyntheticDB',username='synthetic-reader'))); config.chmod(0o600)
    password=directory/'sql-password'; password.write_text('synthetic-offline-password'); password.chmod(0o600)
    path=str(directory/'mcp.sock')
    env=dict(os.environ,CREDENTIALS_DIRECTORY=str(directory))
    def start():
        process=subprocess.Popen([exe,'--serve',str(config),'--socket',path],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        deadline=time.monotonic()+15
        while not pathlib.Path(path).exists():
            if process.poll() is not None or time.monotonic()>deadline: raise AssertionError('socket startup')
            time.sleep(.05)
        return process
    def status():
        # Bridge has NO SQL/tunnel credentials environment.
        bridge_env=dict(os.environ); bridge_env.pop('CREDENTIALS_DIRECTORY',None)
        bridge=subprocess.Popen([exe,'--bridge',path],env=bridge_env,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
        inbox=queue.Queue()
        def read():
            for line in bridge.stdout: inbox.put(json.loads(line))
        threading.Thread(target=read,daemon=True).start()
        def call(ident,method,params):
            bridge.stdin.write(json.dumps(dict(jsonrpc='2.0',id=ident,method=method,params=params))+'\n'); bridge.stdin.flush()
            while True:
                reply=inbox.get(timeout=15)
                if reply.get('id')==ident: return reply['result']
        try:
            call(1,'initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='socket-smoke',version='1')))
            bridge.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); bridge.stdin.flush()
            result=call(2,'tools/call',dict(name='get_server_status',arguments={}))
            data=json.loads(result['content'][0]['text']); assert data['instanceId']=='tenant-a' and not data['mutationsEnabled']
            return data['runtimeId']
        finally:
            bridge.terminate(); bridge.wait(timeout=10)
    runtime=start()
    try:
        first=status(); assert status()==first, 'Bridge restart must not restart SQL runtime'
        runtime.terminate(); runtime.wait(timeout=15)
        assert runtime.returncode==0 and not pathlib.Path(path).exists(), 'graceful stop cleans socket'
        assert config.exists() and password.exists(), 'persistent files survive runtime stop'
        runtime=start(); assert status()!=first, 'Runtime restart must have a new capture/session identity'
        print('PASS: independent bridge reconnect, stable runtime across bridge restarts, graceful SIGTERM/socket cleanup and runtime restart')
    finally:
        runtime.terminate(); runtime.wait(timeout=15)
