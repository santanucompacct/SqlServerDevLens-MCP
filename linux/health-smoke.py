"""Health aggregation fixture; no SQL or tunnel network calls."""
import importlib.util, io, json, pathlib, subprocess, tempfile, unittest, unittest.mock

spec=importlib.util.spec_from_file_location('health',pathlib.Path(__file__).with_name('devlens-health.py'))
health=importlib.util.module_from_spec(spec); spec.loader.exec_module(health)

class HealthTests(unittest.TestCase):
    def run_health(self, wrong_identity=False, sql_error=False, poll_error=False):
        with tempfile.TemporaryDirectory() as tmp:
            directory=pathlib.Path(tmp)/'tenant-a'; directory.mkdir()
            (directory/'instance.json').write_text('{"database":"SyntheticDB"}')
            (directory/'dashboard-port').write_text('18080')
            status=dict(instanceId='wrong' if wrong_identity else 'tenant-a',database='SyntheticDB',mutationsEnabled=False)
            messages=[dict(id=1,result={}),dict(id=2,result={'content':[{'text':json.dumps(status)}]}),dict(id=3,result={'isError':sql_error})]
            buffer=io.BytesIO(b''.join((json.dumps(m)+'\n').encode() for m in messages))
            channel=unittest.mock.Mock(); channel.readline.side_effect=buffer.readline
            client=unittest.mock.MagicMock(); client.__enter__.return_value=client
            client.makefile.return_value.__enter__.return_value=channel
            def command(args,**kwargs):
                if poll_error and 'health' in args: raise subprocess.CalledProcessError(1,args)
                return subprocess.CompletedProcess(args,0)
            with unittest.mock.patch.object(health.socket,'socket',return_value=client), unittest.mock.patch.object(health.subprocess,'run',side_effect=command):
                return health.health(directory)
    def test_ready_requires_all_layers(self): self.assertEqual(('tenant-a',True),self.run_health())
    def test_wrong_runtime_is_rejected(self): self.assertEqual(('tenant-a',False),self.run_health(wrong_identity=True))
    def test_sql_failure_is_rejected(self): self.assertEqual(('tenant-a',False),self.run_health(sql_error=True))
    def test_tunnel_failure_is_rejected(self): self.assertEqual(('tenant-a',False),self.run_health(poll_error=True))

if __name__=='__main__': unittest.main()
