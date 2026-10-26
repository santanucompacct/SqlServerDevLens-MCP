using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SqlServerDevLens.Headless;

internal static class SystemdCredentialAcl
{
    [DllImport("libc")] private static extern uint geteuid();
    public static bool IsPrivate(string path, bool directory)
    {
        try
        {
            if (!OperatingSystem.IsLinux() || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            var info = new ProcessStartInfo("/usr/bin/getfacl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--numeric", "--absolute-names", "--", path }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { process.Kill(); return false; }
            if (process.ExitCode != 0 || !output.Wait(1000)) return false;
            var text = output.Result; if (text.Length > 8192) return false;
            var uid = geteuid().ToString();
            if (!text.Contains("# owner: 0\n", StringComparison.Ordinal) && !text.Contains($"# owner: {uid}\n", StringComparison.Ordinal)) return false;
            var readGrant = false;
            foreach (var line in text.Split('\n').Select(l => l.Trim()))
            {
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var entry = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0].Split(':');
                if (entry.Length != 3 || entry[2].Length != 3 || entry[2][1] == 'w') return false;
                switch (entry[0])
                {
                    case "user":
                        if (entry[1] != "" && entry[1] != uid && entry[2] != "---") return false;
                        if (entry[1] == uid && entry[2][0] == 'r' && (!directory || entry[2][2] == 'x')) readGrant = true;
                        break;
                    case "group": case "other":
                        if (entry[2] != "---") return false;
                        break;
                    case "mask": break;
                    default: return false;
                }
            }
            return readGrant;
        }
        catch { return false; }
    }
}
