#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;

namespace UnityVolumeRendering.Tests
{
    /// <summary>
    /// A Wave server that accepts connections and never answers a byte — the
    /// failure the client's 45 s stall window exists for.
    ///
    /// It lives in its own OS process on purpose: the guard that uses it runs
    /// inside Play Mode, and entering Play Mode reloads the domain, which would
    /// collect a managed listener and close the port (the client then reports
    /// "cannot connect", which is a different failure altogether).
    /// </summary>
    public static class SlabLabSilentServer
    {
        public const int Port = 8099;
        private static int pid;
        private static string scriptPath;

        public static bool IsRunning { get { return pid > 0; } }

        public static bool Start()
        {
            if (pid > 0)
                return true;
            scriptPath = Path.Combine(Path.GetTempPath(),
                "slablab-silent-server.py");
            string script =
                "import socket\n" +
                "s = socket.socket()\n" +
                "s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)\n" +
                "s.bind(('127.0.0.1', " + Port + "))\n" +
                "s.listen(16)\n" +
                "held = []\n" +
                "while True:\n" +
                "    held.append(s.accept()[0])\n";
            try
            {
                // A file rather than -c: quoting a multi-line script through
                // ProcessStartInfo is how one ends up debugging quoting.
                File.WriteAllText(scriptPath, script);
                var info = new ProcessStartInfo
                {
                    FileName = "/usr/bin/python3",
                    Arguments = "\"" + scriptPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                Process server = Process.Start(info);
                if (server == null)
                    return false;
                if (server.WaitForExit(1500))
                    return false;          // no python, or the port is taken
                pid = server.Id;
                UnityEngine.Debug.Log("Silent Wave server on port " + Port +
                    " (PID " + pid + ").");
                return true;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(
                    "Silent Wave server did not start: " + exception.Message);
                return false;
            }
        }

        public static void Stop()
        {
            if (pid > 0)
            {
                try
                {
                    Process.GetProcessById(pid).Kill();
                }
                catch (Exception)
                {
                    // Already gone; the port is free either way.
                }
                pid = 0;
            }
            if (!string.IsNullOrEmpty(scriptPath))
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch (IOException)
                {
                }
                scriptPath = null;
            }
        }
    }
}
#endif
