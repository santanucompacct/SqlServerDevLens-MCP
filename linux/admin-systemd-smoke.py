#!/usr/bin/env python3
"""Disposable Ubuntu systemd integration: real root-owned files and locked-down admin service."""
import grp
import http.cookiejar
import json
import os
import pathlib
import re
import shutil
import socket
import signal
import stat
import subprocess
import sys
import time
import urllib.parse
import urllib.request

if os.geteuid() != 0 or os.environ.get("DEVLENS_DISPOSABLE_CI") != "1":
    sys.exit(64)

source=pathlib.Path("/etc/devlens-admin")
credential=source/"admin-token"
source.mkdir(mode=0o700,parents=True,exist_ok=True)
os.chmod(source,0o700)
credential.write_text("synthetic-ci-operator-secret-12345")
os.chmod(credential,0o400)
def command(*args):
    return subprocess.run(args,check=True,timeout=50,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
def post(opener,endpoint,data):
    request=urllib.request.Request("http://127.0.0.1:8090"+endpoint,
        data=urllib.parse.urlencode(data, doseq=True).encode(),method="POST")
    return opener.open(request,timeout=45).read()
try:
    command("systemctl","daemon-reload")
    command("systemctl","start","devlens-admin.service")
    cookies=http.cookiejar.CookieJar()
    opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))
    for _ in range(80):
        try:
            response=opener.open("http://127.0.0.1:8090/login",timeout=1)
            if response.status==200: break
        except OSError: pass
        time.sleep(0.2)
    else: raise RuntimeError("systemd-hosted admin portal did not listen")
    post(opener,"/login",dict(token=credential.read_text()))
    page=opener.open("http://127.0.0.1:8090/instance/ci-alpha").read().decode()
    assert "Configure ci-alpha" in page, "Tenant config was unreadable under systemd sandbox"
    match=re.search(r"name='csrf' value='([0-9A-F]{64})'",page)
    assert match,"CSRF token not rendered"
    csrf=match.group(1)
    post(opener,"/instance/ci-alpha/policy",dict(csrf=csrf,mode="full",category=["procedure_execute"],hours="1"))
    policy=pathlib.Path("/etc/devlens/ci-alpha/operator.json")
    obj=json.loads(policy.read_text())
    assert obj["mode"]=="full" and obj["automaticCategories"]==["procedure_execute"]
    assert stat.S_IMODE(policy.stat().st_mode)==0o440
    assert policy.stat().st_uid==0 and policy.stat().st_gid==grp.getgrnam("devlens-ci-alpha").gr_gid
    post(opener,"/instance/ci-alpha/secret",dict(csrf=csrf,kind="postman-key",value="synthetic-ci-postman-key"))
    secret=pathlib.Path("/etc/devlens/ci-alpha/postman-key")
    assert stat.S_IMODE(secret.stat().st_mode)==0o440
    assert secret.stat().st_gid==grp.getgrnam("devlens-ci-alpha").gr_gid
    command("runuser","-u","devlens-ci-alpha","--","test","-r",str(secret))
    assert subprocess.run(["runuser","-u","devlens-ci-beta","--","test","-r",str(secret)]).returncode != 0
    conf=pathlib.Path("/etc/devlens/ci-alpha/instance.json")
    data=json.loads(conf.read_text())
    post(opener,"/instance/ci-alpha/db",dict(csrf=csrf,host=data["host"],port=data["port"],database=data["database"],username=data["username"]))
    assert stat.S_IMODE(conf.stat().st_mode)==0o400
    assert conf.stat().st_uid==__import__("pwd").getpwnam("devlens-ci-alpha").pw_uid
    # Live offline-runtime acceptance: a restart must load the saved private operator
    # policy and expose the existing SQL prepare/apply tools without SQL connectivity.
    command("systemctl","start","devlens@ci-alpha.service")
    endpoint="/run/devlens-ci-alpha/mcp.sock"
    for _ in range(80):
        if pathlib.Path(endpoint).is_socket(): break
        time.sleep(0.2)
    else: raise RuntimeError("Full-mode SQL runtime did not create its socket")
    with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as client:
        client.settimeout(10)
        client.connect(endpoint)
        with client.makefile("rwb",buffering=0) as channel:
            def request(ident,method,params):
                payload=json.dumps(dict(jsonrpc="2.0",id=ident,method=method,params=params))
                channel.write((payload+"\n").encode())
                for _ in range(20):
                    result=json.loads(channel.readline(1024*1024))
                    if result.get("id")==ident: return result["result"]
                raise RuntimeError("Missing MCP response")
            request(1,"initialize",dict(protocolVersion="2025-11-25",capabilities={},
                clientInfo=dict(name="admin-systemd-ci",version="1")))
            channel.write(b'{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
            tools=request(2,"tools/list",{})
            names={tool["name"] for tool in tools["tools"]}
            assert "prepare_procedure_execution" in names and "apply_table_sql" in names
            status=request(3,"tools/call",dict(name="get_server_status",arguments={}))
            live=json.loads(status["content"][0]["text"])
            assert live["mode"]=="linux-headless-full"
            assert live["instanceId"]=="ci-alpha"
            assert live["authorizedCategories"]["procedure_execute"] is True
            assert live["authorizedCategories"]["table_delete"] is False
    command("systemctl","stop","devlens@ci-alpha.service")
    # This is a disposable runner. Verify the Add Instance UI invokes systemd-managed provisioning.
    post(opener,"/new",dict(csrf=csrf,name="ci-gamma",tunnel="tunnel_00000000000000000000000000000003",port="18082"))
    assert pathlib.Path("/etc/devlens/ci-gamma/instance.json").exists(),"Portal did not provision a new Linux tenant"
    print("PASS: systemd admin authentication, operator policy, protected credential ACL, instance update and new tenant provision")
finally:
    subprocess.run(["systemctl","stop","devlens@ci-alpha.service"],check=False,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    subprocess.run(["systemctl","stop","devlens-admin.service"],check=False,
        stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    for path in ["/etc/devlens/ci-alpha/operator.json","/etc/devlens/ci-alpha/postman-key"]:
        pathlib.Path(path).unlink(missing_ok=True)
    if pathlib.Path("/etc/devlens/ci-gamma").is_dir():
        shutil.rmtree("/etc/devlens/ci-gamma")
    if pathlib.Path("/var/lib/devlens-ci-gamma").is_dir():
        shutil.rmtree("/var/lib/devlens-ci-gamma")
    subprocess.run(["userdel","devlens-ci-gamma"],check=False,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    shutil.rmtree(source,ignore_errors=True)
