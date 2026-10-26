"""Offline stdio test. Synthetic unreachable config; never opens SQL or an external tunnel."""
import json, os, pathlib, queue, signal, subprocess, sys, tempfile, threading, time

expected = {'get_server_status', 'get_database_info', 'list_database_objects', 'describe_table',
            'get_table_schema', 'read_table_page', 'query_read_only', 'get_procedure_definition',
            'read_result_page', 'release_result'}
forbidden = ['prepare_procedure_execution', 'execute_prepared_procedure', 'prepare_procedure_change',
             'apply_procedure_change', 'prepare_table_sql', 'apply_table_sql', 'run_postman_request']
with tempfile.TemporaryDirectory(prefix='devlens-headless-') as tmp:
    children = []
    try:
        for name in ['tenant-a', 'tenant-b']:
            directory = pathlib.Path(tmp)/name; directory.mkdir(mode=0o700)
            config = directory/'instance.json'
            config.write_text(json.dumps(dict(instanceId=name, host='localhost', port=1,
                                             database='DB-'+name, username='synthetic-reader')))
            config.chmod(0o600)
            secret = directory/'sql-password'; secret.write_text('synthetic-offline-password'); secret.chmod(0o600)
            env = dict(os.environ, CREDENTIALS_DIRECTORY=str(directory))
            command = [sys.argv[1]] if not sys.argv[1].endswith('.dll') else ['dotnet', sys.argv[1]]
            child = subprocess.Popen(command+['--config', str(config)], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, env=env)
            replies = queue.Queue()
            def read(process, inbox):
                for line in process.stdout:
                    inbox.put(json.loads(line))  # stdout pollution must fail the test
            threading.Thread(target=read, args=(child,replies), daemon=True).start()
            children.append((child,replies,name))
        def call(child, inbox, ident, method, params):
            child.stdin.write(json.dumps(dict(jsonrpc='2.0',id=ident,method=method,params=params))+'\n'); child.stdin.flush()
            deadline=time.monotonic()+20
            while time.monotonic()<deadline:
                reply=inbox.get(timeout=20)
                if reply.get('id')==ident: return reply
            raise AssertionError('MCP timeout')
        for child,inbox,name in children:
            assert 'result' in call(child,inbox,1,'initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='offline-test',version='1')))
            child.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); child.stdin.flush()
            tools=call(child,inbox,2,'tools/list',{})['result']['tools']
            assert {tool['name'] for tool in tools}==expected
            status=call(child,inbox,3,'tools/call',dict(name='get_server_status',arguments={}))['result']
            data=json.loads(status['content'][0]['text'])
            assert data['instanceId']==name and data['database']=='DB-'+name and data['mutationsEnabled'] is False
            for i,tool in enumerate(forbidden,10):
                reply=call(child,inbox,i,'tools/call',dict(name=tool,arguments={}))
                assert 'error' in reply or reply.get('result',{}).get('isError') is True
            reply=call(child,inbox,30,'tools/call',dict(name='query_read_only',arguments=dict(sql='DELETE FROM dbo.Example WHERE id=1')))
            assert 'error' in reply or reply.get('result',{}).get('isError') is True
            if sys.platform.startswith('linux'):
                status_text=pathlib.Path(f'/proc/{child.pid}/status').read_text()
                print(name, next(line for line in status_text.splitlines() if line.startswith('VmRSS:')))
        print('PASS: two isolated identities, 10 read tools, writes unavailable, no SQL executed')
    finally:
        for child,_,_ in children:
            child.terminate()
        for child,_,_ in children:
            try: child.wait(timeout=10)
            except subprocess.TimeoutExpired: child.kill(); child.wait(); raise
