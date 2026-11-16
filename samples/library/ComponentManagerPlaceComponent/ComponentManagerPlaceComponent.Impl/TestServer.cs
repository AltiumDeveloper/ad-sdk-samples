using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AdSamples.ComponentManagerPlaceComponent
{
    /// <summary>What the stub server answers: the two library files and the metadata sent with them.</summary>
    internal sealed class TestServerConfig
    {
        public string SchLibPath { get; init; }
        public string PcbLibPath { get; init; }
        public string Id { get; init; }
        public string ComponentType { get; init; }
        public string AssembleTech { get; init; }
        public string LibraryPath { get; init; }
        public string LibraryRef { get; init; }
        public string FootprintPath { get; init; }
        public string FootprintRef { get; init; }
        public IReadOnlyList<ComponentParameter> Parameters { get; init; }
    }

    /// <summary>
    /// Test setup: a minimal HTTP/1.1 server on 127.0.0.1 that speaks the component
    /// server protocol (see ComponentServer), so the place commands can be run without a real component management
    /// server. A TcpListener instead of HttpListener: HttpListener needs a URL reservation (netsh) for non-admin users
    /// on some machines; a loopback socket does not. One request at a time, on a background thread that never touches
    /// the Altium API. Stopped by StopTestServer and before the implementation is unloaded (SampleHooks).
    /// </summary>
    internal sealed class TestServer
    {
        private static readonly object Gate = new object();
        public static TestServer Current { get; private set; }

        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly TestServerConfig _config;
        private int _requests;
        private volatile string _lastRequest = "";

        public int Port { get; }
        public int Requests => Volatile.Read(ref _requests);
        public string LastRequest => _lastRequest;
        public TestServerConfig Config => _config;
        public string Url => $"http://127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}/";

        private TestServer(TestServerConfig config, int port)
        {
            _config = config;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Run) { IsBackground = true, Name = "ComponentManagerPlaceComponent test server" };
            _thread.Start();
        }

        /// <summary>Starts a server (stopping a running one first). Port 0 picks a free port.</summary>
        public static TestServer Start(TestServerConfig config, int port)
        {
            lock (Gate)
            {
                Current?.StopCore();
                Current = null;
                Current = new TestServer(config, port);
                return Current;
            }
        }

        /// <summary>Stops the running server; returns it (for its counters), or null if none was running.</summary>
        public static TestServer Stop()
        {
            lock (Gate)
            {
                TestServer server = Current;
                Current = null;
                server?.StopCore();
                return server;
            }
        }

        private void StopCore()
        {
            // Stop() makes the blocking AcceptTcpClient throw, which ends the loop. Join so that no thread runs code
            // of this assembly after it has been unloaded.
            try { _listener.Stop(); } catch (SocketException) { }
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        private void Run()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception e) when (e is SocketException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    return; // stopped
                }

                try
                {
                    using (client)
                        Serve(client);
                }
                catch (Exception e) when (e is IOException || e is SocketException)
                {
                    // A client that hangs up is not the server's problem.
                }
            }
        }

        private void Serve(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            NetworkStream stream = client.GetStream();
            string requestLine = ReadRequestHead(stream);
            Interlocked.Increment(ref _requests);
            _lastRequest = requestLine;

            // "GET /path?query HTTP/1.1"
            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2 || parts[0] != "GET")
            {
                Respond(stream, 405, "Method Not Allowed", null, Encoding.ASCII.GetBytes("GET only"));
                return;
            }

            string target = parts[1];
            int q = target.IndexOf('?');
            string path = q < 0 ? target : target.Substring(0, q);
            Dictionary<string, string> query = ParseQuery(q < 0 ? "" : target.Substring(q + 1));

            bool pcbLib;
            if (path.EndsWith("/Home/DownloadPcbLibFile", StringComparison.OrdinalIgnoreCase))
                pcbLib = true;
            else if (path.EndsWith("/Home/DownloadSchLibFile", StringComparison.OrdinalIgnoreCase))
                pcbLib = false;
            else
            {
                Respond(stream, 404, "Not Found", null, Encoding.ASCII.GetBytes("unknown path " + path));
                return;
            }

            // Only the configured component is known; any other id answers 404, to try the client's error path.
            if (!query.TryGetValue("componentId", out string componentId) || componentId != _config.Id ||
                !query.TryGetValue("sessionId", out string sessionId) || sessionId.Length == 0)
            {
                Respond(stream, 404, "Not Found", null, Encoding.ASCII.GetBytes("unknown componentId or no sessionId"));
                return;
            }

            string file = pcbLib ? _config.PcbLibPath : _config.SchLibPath;
            byte[] body;
            // AD may hold the library open: read with sharing.
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                body = ms.ToArray();
            }
            Respond(stream, 200, "OK", Disposition(Path.GetFileName(file)), body);
        }

        /// <summary>The metadata header in the format ComponentServer parses. Values are percent-encoded.</summary>
        private string Disposition(string fileName)
        {
            var sb = new StringBuilder("attachment");
            void Add(string key, string value) => sb.Append("; ").Append(key).Append('=').Append(value);
            Add("filename", fileName);
            Add("id", _config.Id);
            Add("componenttype", Uri.EscapeDataString(_config.ComponentType));
            Add("assembletech", Uri.EscapeDataString(_config.AssembleTech));
            Add("librarypath", Uri.EscapeDataString(_config.LibraryPath));
            Add("libraryref", Uri.EscapeDataString(_config.LibraryRef));
            Add("footprintpath", Uri.EscapeDataString(_config.FootprintPath));
            Add("footprintref", Uri.EscapeDataString(_config.FootprintRef));
            Add("ParameterCount", _config.Parameters.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < _config.Parameters.Count; i++)
            {
                string n = i.ToString(CultureInfo.InvariantCulture);
                Add("ParameterName_" + n, Uri.EscapeDataString(_config.Parameters[i].Name));
                Add("ParameterType_" + n, Uri.EscapeDataString(_config.Parameters[i].Type));
                Add("ParameterValue_" + n, Uri.EscapeDataString(_config.Parameters[i].Value));
            }
            return sb.ToString();
        }

        private static void Respond(NetworkStream stream, int status, string reason, string disposition, byte[] body)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(reason).Append("\r\n");
            head.Append("Content-Type: application/octet-stream\r\n");
            if (disposition != null)
                head.Append("Content-Disposition: ").Append(disposition).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            head.Append("Connection: close\r\n\r\n");
            byte[] headBytes = Encoding.ASCII.GetBytes(head.ToString());
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        /// <summary>Reads up to the blank line that ends the request head; returns the request line.</summary>
        private static string ReadRequestHead(NetworkStream stream)
        {
            var bytes = new List<byte>();
            int b;
            while ((b = stream.ReadByte()) >= 0 && bytes.Count < 16384)
            {
                bytes.Add((byte)b);
                int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
                    break;
            }
            string head = Encoding.ASCII.GetString(bytes.ToArray());
            int end = head.IndexOf("\r\n", StringComparison.Ordinal);
            return end < 0 ? head : head.Substring(0, end);
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(eq < 0 ? pair : pair.Substring(0, eq));
                string value = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1));
                result.TryAdd(key, value);
            }
            return result;
        }
    }
}
