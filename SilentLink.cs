using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SilentLink {
    class Program {
        // --- Paths (set once at startup) ---
        static string BaseDir;
        static string ConfigFile;
        static string CommandsFile;
        static string LogFile;

        // --- Config cache ---
        static string AuthToken    = "MyMacroDroidSecretKey2026";
        static int    Port         = 5000;
        static bool   RestrictLocal = true;
        static DateTime _configLastWrite = DateTime.MinValue;
        static readonly object _configLock = new object();

        // --- Commands cache ---
        static Dictionary<string, string> _commandsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static DateTime _commandsLastWrite = DateTime.MinValue;
        static readonly object _commandsLock = new object();

        // --- Log writer (kept open, not re-opened per line) ---
        static StreamWriter _logWriter;
        static readonly object _logLock = new object();

        // --- Safe working dir (computed once) ---
        static string _safeDir;

        // ------------------------------------------------------------------ //

        static void Main(string[] args) {
            try {
                BaseDir      = AppDomain.CurrentDomain.BaseDirectory;
                ConfigFile   = Path.Combine(BaseDir, "config.json");
                CommandsFile = Path.Combine(BaseDir, "commands.json");
                LogFile      = Path.Combine(BaseDir, "server.log");

                _logWriter = new StreamWriter(
                    new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                    Encoding.UTF8) { AutoFlush = true };

                _safeDir = ComputeSafeDir();

                LoadConfigIfChanged();

                Log("************************************************************");
                Log(" SilentLink Server is RUNNING on port " + Port);
                Log("************************************************************");

                TcpListener listener = new TcpListener(IPAddress.Any, Port);
                // Allow fast restart without "port already in use" delay
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();

                while (true) {
                    try {
                        TcpClient client = listener.AcceptTcpClient();
                        // Disable Nagle — we always send full responses at once
                        client.NoDelay = true;
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    } catch (Exception ex) {
                        Log("[ACCEPT EXCEPTION] " + ex.Message);
                        Thread.Sleep(100);
                    }
                }
            } catch (Exception ex) {
                Log("[MAIN CRASH] " + ex.ToString());
            }
        }

        // ------------------------------------------------------------------ //
        // Config — only re-reads file when it has actually changed on disk
        // ------------------------------------------------------------------ //

        static void LoadConfigIfChanged() {
            if (!File.Exists(ConfigFile)) return;
            try {
                DateTime wt = File.GetLastWriteTimeUtc(ConfigFile);
                lock (_configLock) {
                    if (wt <= _configLastWrite) return;
                    string json = File.ReadAllText(ConfigFile, Encoding.UTF8);

                    Match mToken = Regex.Match(json, "\"auth_token\"\\s*:\\s*\"([^\"]+)\"");
                    if (mToken.Success) AuthToken = mToken.Groups[1].Value;

                    Match mPort = Regex.Match(json, "\"port\"\\s*:\\s*(\\d+)");
                    if (mPort.Success) Port = int.Parse(mPort.Groups[1].Value);

                    Match mLocal = Regex.Match(json, "\"restrict_to_local_network\"\\s*:\\s*(true|false)");
                    if (mLocal.Success) RestrictLocal = bool.Parse(mLocal.Groups[1].Value);

                    _configLastWrite = wt;
                }
            } catch {}
        }

        // ------------------------------------------------------------------ //
        // Commands — only re-reads file when it has actually changed on disk
        // ------------------------------------------------------------------ //

        static string FindCommand(string action) {
            if (!File.Exists(CommandsFile)) return null;
            try {
                DateTime wt = File.GetLastWriteTimeUtc(CommandsFile);
                lock (_commandsLock) {
                    if (wt > _commandsLastWrite) {
                        string json = File.ReadAllText(CommandsFile, Encoding.UTF8);
                        var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        // Match { "action": { "path": "..." } }
                        MatchCollection rich = Regex.Matches(json,
                            "\"([^\"]+)\"\\s*:\\s*\\{[^}]*\"path\"\\s*:\\s*\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);
                        foreach (Match m in rich)
                            fresh[m.Groups[1].Value] = m.Groups[2].Value.Replace("\\\\", "\\");

                        // Match { "action": "path" } (simple string value, not already in dict)
                        MatchCollection simple = Regex.Matches(json,
                            "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\"",
                            RegexOptions.IgnoreCase);
                        foreach (Match m in simple)
                            if (!fresh.ContainsKey(m.Groups[1].Value))
                                fresh[m.Groups[1].Value] = m.Groups[2].Value.Replace("\\\\", "\\");

                        _commandsCache = fresh;
                        _commandsLastWrite = wt;
                    }
                    string val;
                    return _commandsCache.TryGetValue(action, out val) ? val : null;
                }
            } catch { return null; }
        }

        // ------------------------------------------------------------------ //
        // Logging — single open writer, one lock per line
        // ------------------------------------------------------------------ //

        static void Log(string msg) {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg;
            lock (_logLock) {
                try { _logWriter.WriteLine(line); } catch {}
            }
        }

        // ------------------------------------------------------------------ //
        // IP helper
        // ------------------------------------------------------------------ //

        static bool IsPrivateIp(IPAddress ip) {
            if (IPAddress.IsLoopback(ip)) return true;
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        // ------------------------------------------------------------------ //
        // HTTP helpers
        // ------------------------------------------------------------------ //

        static readonly byte[] _sharedSendBuf = new byte[4096];

        static void SendResponse(NetworkStream stream, int statusCode, string statusText, string jsonBody) {
            byte[] body = Encoding.UTF8.GetBytes(jsonBody);
            string header = string.Format(
                "HTTP/1.1 {0} {1}\r\nContent-Type: application/json\r\nContent-Length: {2}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n",
                statusCode, statusText, body.Length);
            byte[] head = Encoding.ASCII.GetBytes(header);
            // Write header + body as one call when small enough
            if (head.Length + body.Length <= _sharedSendBuf.Length) {
                Buffer.BlockCopy(head, 0, _sharedSendBuf, 0, head.Length);
                Buffer.BlockCopy(body, 0, _sharedSendBuf, head.Length, body.Length);
                stream.Write(_sharedSendBuf, 0, head.Length + body.Length);
            } else {
                stream.Write(head, 0, head.Length);
                stream.Write(body, 0, body.Length);
            }
            stream.Flush();
        }

        // ------------------------------------------------------------------ //
        // Request handler
        // ------------------------------------------------------------------ //

        static void HandleClient(object state) {
            TcpClient client = (TcpClient)state;
            try {
                using (client) {
                    NetworkStream stream = client.GetStream();

                    // Read the full request (handle partial reads)
                    byte[] buffer = new byte[8192];
                    int total = 0;
                    int read;
                    while (total < buffer.Length &&
                           (read = stream.Read(buffer, total, buffer.Length - total)) > 0) {
                        total += read;
                        // Stop once we have the full HTTP header (ends with blank line)
                        string so_far = Encoding.UTF8.GetString(buffer, 0, total);
                        if (so_far.Contains("\r\n\r\n") || so_far.Contains("\n\n")) break;
                    }
                    if (total <= 0) return;

                    string raw = Encoding.UTF8.GetString(buffer, 0, total);

                    // Check config reload (cheap: just compares timestamp)
                    LoadConfigIfChanged();

                    // --- IP restriction ---
                    IPAddress clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                    if (RestrictLocal && !IsPrivateIp(clientIp)) {
                        Log("[BLOCKED] Non-local IP: " + clientIp);
                        SendResponse(stream, 403, "Forbidden",
                            "{\"status\":\"error\",\"message\":\"Forbidden: Non-local IP\"}");
                        return;
                    }

                    // --- Parse request line ---
                    int firstLine = raw.IndexOf('\n');
                    if (firstLine < 0) return;
                    string[] reqParts = raw.Substring(0, firstLine).TrimEnd().Split(' ');
                    if (reqParts.Length < 2) return;
                    string method = reqParts[0].ToUpperInvariant();
                    string path   = reqParts[1];

                    // --- Parse headers ---
                    string tokenReceived = null;
                    int headerEnd = raw.IndexOf("\r\n\r\n");
                    if (headerEnd < 0) headerEnd = raw.IndexOf("\n\n");
                    if (headerEnd < 0) return;

                    string headerSection = raw.Substring(0, headerEnd);
                    string[] headerLines = headerSection.Split('\n');
                    for (int i = 1; i < headerLines.Length; i++) {
                        string h = headerLines[i].TrimEnd('\r');
                        if (h.Length == 0) break;
                        if (tokenReceived == null) {
                            if (h.StartsWith("X-Auth-Token:", StringComparison.OrdinalIgnoreCase)) {
                                tokenReceived = h.Substring(13).Trim();
                            } else if (h.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) {
                                string v = h.Substring(14).Trim();
                                if (v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                                    tokenReceived = v.Substring(7).Trim();
                            }
                        }
                    }

                    // Query-string token fallback for GET
                    if (tokenReceived == null && path.IndexOf("token=", StringComparison.Ordinal) >= 0) {
                        Match mTok = Regex.Match(path, "token=([^&]+)");
                        if (mTok.Success) tokenReceived = mTok.Groups[1].Value;
                    }

                    // --- Auth ---
                    if (tokenReceived == null || tokenReceived != AuthToken) {
                        Log("[BLOCKED] Unauthorized from " + clientIp);
                        SendResponse(stream, 401, "Unauthorized",
                            "{\"status\":\"error\",\"message\":\"Unauthorized: Missing or invalid token.\"}");
                        return;
                    }

                    // --- GET: health check ---
                    if (method == "GET") {
                        if (path.StartsWith("/run", StringComparison.Ordinal) || path.IndexOf("action=", StringComparison.Ordinal) >= 0) {
                            SendResponse(stream, 405, "Method Not Allowed",
                                "{\"status\":\"error\",\"message\":\"Use POST /run.\"}");
                        } else {
                            Log("[GET] Ping from " + clientIp);
                            SendResponse(stream, 200, "OK",
                                "{\"status\":\"ok\",\"message\":\"pong\",\"client_ip\":\"" + clientIp + "\"}");
                        }
                        return;
                    }

                    // --- POST: run action ---
                    if (method == "POST") {
                        int bodyStart = headerEnd + (raw[headerEnd] == '\r' ? 4 : 2);
                        string body = bodyStart < raw.Length ? raw.Substring(bodyStart) : "";

                        Match mAction = Regex.Match(body, "\"action\"\\s*:\\s*\"([^\"]+)\"");
                        if (!mAction.Success) {
                            SendResponse(stream, 400, "Bad Request",
                                "{\"status\":\"error\",\"message\":\"Missing 'action'.\"}");
                            return;
                        }

                        string action = mAction.Groups[1].Value;
                        Log("[POST] Action '" + action + "' from " + clientIp);

                        string cmdPath = FindCommand(action);
                        if (cmdPath == null) {
                            SendResponse(stream, 404, "Not Found",
                                "{\"status\":\"error\",\"action\":\"" + action + "\",\"message\":\"Action not defined.\"}");
                            return;
                        }

                        if (LaunchCommand(cmdPath)) {
                            Log("[LAUNCHED] " + cmdPath);
                            SendResponse(stream, 200, "OK",
                                "{\"status\":\"ok\",\"action\":\"" + action + "\",\"message\":\"Launched successfully\"}");
                        } else {
                            SendResponse(stream, 500, "Internal Error",
                                "{\"status\":\"error\",\"message\":\"Failed to launch.\"}");
                        }
                        return;
                    }

                    SendResponse(stream, 405, "Method Not Allowed",
                        "{\"status\":\"error\",\"message\":\"Unsupported method.\"}");
                }
            } catch {}
        }

        // ------------------------------------------------------------------ //
        // Process launch helpers
        // ------------------------------------------------------------------ //

        static string ComputeSafeDir() {
            try {
                string p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                string s = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrEmpty(s) && Directory.Exists(s)) return s;
            } catch {}
            return @"C:\";
        }

        static void ParseCommand(string fullCmd, out string file, out string args) {
            string t = (fullCmd ?? "").Trim();
            if (t.StartsWith("cmd.exe /c start", StringComparison.OrdinalIgnoreCase)) {
                t = t.Substring(16).Trim();
                if (t.StartsWith("\"\"")) t = t.Substring(2).Trim();
            }
            if (t.StartsWith("\"")) {
                int q = t.IndexOf('"', 1);
                if (q > 0) { file = t.Substring(1, q - 1); args = t.Substring(q + 1).Trim(); return; }
                file = t.Trim('"'); args = ""; return;
            }
            int sp = t.IndexOf(' ');
            if (sp > 0) { file = t.Substring(0, sp); args = t.Substring(sp + 1).Trim(); return; }
            file = t; args = "";
        }

        static bool LaunchViaExplorer(string file, string args) {
            try {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;
                object shell   = Activator.CreateInstance(shellType);
                object windows = shellType.InvokeMember("Windows",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                if (windows == null) return false;
                object desktop = windows.GetType().InvokeMember("FindWindowSW",
                    System.Reflection.BindingFlags.InvokeMethod, null, windows,
                    new object[] { 0, 0, 8, 0, 1 });
                if (desktop == null) return false;
                object doc = desktop.GetType().InvokeMember("Document",
                    System.Reflection.BindingFlags.GetProperty, null, desktop, null);
                if (doc == null) return false;
                object app = doc.GetType().InvokeMember("Application",
                    System.Reflection.BindingFlags.GetProperty, null, doc, null);
                if (app == null) return false;
                app.GetType().InvokeMember("ShellExecute",
                    System.Reflection.BindingFlags.InvokeMethod, null, app,
                    new object[] { file, args, _safeDir, "open", 1 });
                return true;
            } catch (Exception ex) {
                Log("[EXPLORER COM] " + ex.Message);
                return false;
            }
        }

        static bool LaunchCommand(string fullCmd) {
            string file, args;
            ParseCommand(fullCmd, out file, out args);

            if (LaunchViaExplorer(file, args)) return true;

            // Fallback 1: ShellExecute via Process API
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = file, Arguments = args,
                    WorkingDirectory = _safeDir, UseShellExecute = true
                });
                return true;
            } catch (Exception ex) { Log("[FALLBACK1] " + ex.Message); }

            // Fallback 2: cmd /c start
            try {
                string combined = string.IsNullOrEmpty(args)
                    ? "\"" + file + "\"" : "\"" + file + "\" " + args;
                Process.Start(new ProcessStartInfo {
                    FileName = "cmd.exe", Arguments = "/c start \"\" " + combined,
                    WorkingDirectory = _safeDir, CreateNoWindow = true, UseShellExecute = false
                });
                return true;
            } catch (Exception ex) { Log("[FALLBACK2] " + ex.Message); return false; }
        }
    }
}
