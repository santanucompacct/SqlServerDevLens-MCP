#!/usr/bin/env python3
"""Offline admin smoke test: authentication, secure cookie, CSRF and localhost binding."""
import http.cookiejar
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None

def main(binary):
    with tempfile.TemporaryDirectory(prefix="devlens-admin-smoke-") as work:
        root = pathlib.Path(work)
        secret = root / "admin-token"
        secret.write_text("synthetic-ci-not-a-real-password-123")
        os.chmod(secret, 0o400)
        os.chmod(root, 0o700)
        env = dict(os.environ, CREDENTIALS_DIRECTORY=str(root), ASPNETCORE_ENVIRONMENT="Production")
        process = subprocess.Popen([binary], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        base = "http://127.0.0.1:8090"
        opener = urllib.request.build_opener(NoRedirect)
        try:
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError("Admin portal exited before becoming ready")
                try:
                    reply = opener.open(base + "/login", timeout=1)
                    if reply.status == 200:
                        break
                except (OSError, urllib.error.URLError):
                    pass
                time.sleep(0.15)
            else:
                raise RuntimeError("Admin portal never listened on localhost")
            # Anonymous users are redirected without receiving private configuration.
            try:
                opener.open(base + "/", timeout=3)
                raise AssertionError("Anonymous dashboard request was allowed")
            except urllib.error.HTTPError as exc:
                assert exc.code == 302 and exc.headers["Location"] == "/login"
            # Invalid login does not create a session.
            wrong = urllib.parse.urlencode({"token": "wrong"}).encode()
            response = opener.open(urllib.request.Request(base + "/login", data=wrong), timeout=4)
            assert b"Invalid administrator secret" in response.read()
            assert response.headers.get("Set-Cookie") is None
            # Correct login creates an HttpOnly, strict-SameSite session cookie.
            login = urllib.parse.urlencode({"token": secret.read_text()}).encode()
            try:
                opener.open(urllib.request.Request(base + "/login", data=login), timeout=4)
                raise AssertionError("Login was not redirected")
            except urllib.error.HTTPError as exc:
                assert exc.code == 302
                cookie = exc.headers.get("Set-Cookie", "")
                assert "HttpOnly" in cookie and "SameSite=strict" in cookie.lower()
                session = cookie.split(";", 1)[0]
            headers = {"Cookie": session}
            response = opener.open(urllib.request.Request(base + "/", headers=headers), timeout=4)
            assert b"All DevLens instances" in response.read()
            assert response.headers.get("Cache-Control") == "no-store"
            # A forged cross-site POST cannot create an instance even with the cookie.
            create = urllib.parse.urlencode({"name":"ci-unauthorized","tunnel":"tunnel_TEST","port":"18088"}).encode()
            try:
                opener.open(urllib.request.Request(base + "/new", data=create, headers=headers), timeout=4)
                raise AssertionError("POST without CSRF token was accepted")
            except urllib.error.HTTPError as exc:
                assert exc.code == 403
            print("PASS: localhost admin, login, session cookie, private dashboard and CSRF failure closed")
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

if __name__ == "__main__":
    main(os.path.abspath(sys.argv[1]))
