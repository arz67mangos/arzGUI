using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoActions.Obs
{
    /// <summary>
    /// The obs-websocket 5 protocol, cut down to the handful of requests this app makes. It is built
    /// into OBS 28 and later, so nothing has to be installed - the user only enables the server under
    /// Tools > WebSocket Server Settings.
    ///
    /// It is a loopback TCP socket, which is why this and not window messages or synthetic hotkeys:
    /// UIPI blocks those from a normal process into an OBS running as administrator, and a socket does
    /// not care about integrity levels in either direction.
    ///
    /// Everything here is synchronous with hard timeouts on purpose: actions run on the watcher
    /// thread, which is blind for the duration (see AutoActionsDaemon.UpdateCurrentProfile).
    /// </summary>
    internal sealed class ObsWebSocket : IDisposable
    {
        const int OpHello = 0;
        const int OpIdentify = 1;
        const int OpIdentified = 2;
        const int OpRequest = 6;
        const int OpRequestResponse = 7;

        // obs-websocket's close code for a failed or missing authentication.
        const int AuthenticationFailed = 4009;

        static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

        readonly ClientWebSocket _socket;
        int _requestId;

        ObsWebSocket(ClientWebSocket socket)
        {
            _socket = socket;
        }

        /// <summary>
        /// Connects and identifies, retrying until <paramref name="waitFor"/> elapses - a profile that
        /// starts OBS and then talks to it gets here before OBS has opened its socket. Returns null
        /// with a reason in <paramref name="error"/>; a wrong password is not retried.
        /// </summary>
        public static ObsWebSocket Open(int port, string password, TimeSpan waitFor, out string error)
        {
            DateTime deadline = DateTime.UtcNow + waitFor;
            while (true)
            {
                bool worthRetrying;
                ObsWebSocket session = TryOpen(port, password, out error, out worthRetrying);
                if (session != null || !worthRetrying || DateTime.UtcNow >= deadline)
                    return session;
                Thread.Sleep(500);
            }
        }

        static ObsWebSocket TryOpen(int port, string password, out string error, out bool worthRetrying)
        {
            worthRetrying = true;
            ClientWebSocket socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("obswebsocket.json");
            try
            {
                if (!socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)),
                        CancellationToken.None).Wait(CallTimeout))
                    throw new TimeoutException("no answer");
            }
            catch (Exception ex)
            {
                socket.Dispose();
                error = string.Format("cannot reach OBS on port {0} ({1}). Is OBS running with its WebSocket server enabled?",
                    port, Innermost(ex).Message);
                return null;
            }

            ObsWebSocket session = new ObsWebSocket(socket);
            try
            {
                JObject hello = session.Receive(CallTimeout);
                if (Op(hello) != OpHello)
                    throw new Exception("OBS did not greet us; is something else listening on that port?");

                JObject identify = new JObject();
                identify["rpcVersion"] = 1;
                identify["eventSubscriptions"] = 0;
                JToken challenge = hello["d"]["authentication"];
                if (challenge != null)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        worthRetrying = false;
                        throw new Exception("OBS asks for a password. Copy it from Tools > WebSocket Server Settings into the arzGUI settings.");
                    }
                    identify["authentication"] = Authenticate(password, (string)challenge["salt"], (string)challenge["challenge"]);
                }
                session.Send(OpIdentify, identify);

                if (Op(session.Receive(CallTimeout)) != OpIdentified)
                    throw new Exception("OBS refused the connection.");
                error = null;
                return session;
            }
            catch (Exception ex)
            {
                if (session.ClosedBecauseOfAuthentication)
                    worthRetrying = false;
                session.Dispose();
                error = Innermost(ex).Message;
                return null;
            }
        }

        bool ClosedBecauseOfAuthentication
        {
            get { return _socket.CloseStatus.HasValue && (int)_socket.CloseStatus.Value == AuthenticationFailed; }
        }

        /// <summary>Sends one request and waits for its response. False means it failed, with why in <paramref name="error"/>.</summary>
        public bool Request(string requestType, JObject requestData, out JObject response, out string error)
        {
            response = null;
            error = null;
            try
            {
                _requestId++;
                string id = _requestId.ToString(CultureInfo.InvariantCulture);
                JObject request = new JObject();
                request["requestType"] = requestType;
                request["requestId"] = id;
                request["requestData"] = requestData ?? new JObject();
                Send(OpRequest, request);

                DateTime deadline = DateTime.UtcNow + CallTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    JObject message = Receive(CallTimeout);
                    if (Op(message) != OpRequestResponse)
                        continue;
                    JObject data = (JObject)message["d"];
                    if ((string)data["requestId"] != id)
                        continue;
                    JToken status = data["requestStatus"];
                    if (status != null && (bool)status["result"])
                    {
                        response = data["responseData"] as JObject;
                        return true;
                    }
                    string comment = status == null ? null : (string)status["comment"];
                    error = requestType + " failed: " + (string.IsNullOrEmpty(comment)
                        ? "code " + (status == null ? "?" : (string)status["code"])
                        : comment);
                    return false;
                }
                error = requestType + " timed out";
                return false;
            }
            catch (Exception ex)
            {
                error = requestType + ": " + Innermost(ex).Message;
                return false;
            }
        }

        public bool Request(string requestType, JObject requestData, out string error)
        {
            JObject ignored;
            return Request(requestType, requestData, out ignored, out error);
        }

        void Send(int op, JObject data)
        {
            JObject message = new JObject();
            message["op"] = op;
            message["d"] = data;
            byte[] bytes = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
            if (!_socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                    .Wait(CallTimeout))
                throw new TimeoutException("OBS did not accept the request");
        }

        JObject Receive(TimeSpan timeout)
        {
            byte[] buffer = new byte[8192];
            StringBuilder text = new StringBuilder();
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                Task<WebSocketReceiveResult> receive = _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left < TimeSpan.Zero || !receive.Wait(left))
                    throw new TimeoutException("OBS did not answer in time");
                WebSocketReceiveResult result = receive.Result;
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new Exception(string.IsNullOrEmpty(_socket.CloseStatusDescription)
                        ? "OBS closed the connection" : "OBS closed the connection: " + _socket.CloseStatusDescription);
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                    return JObject.Parse(text.ToString());
            }
        }

        static int Op(JObject message)
        {
            JToken op = message["op"];
            return op == null ? -1 : (int)op;
        }

        static string Authenticate(string password, string salt, string challenge)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }

        internal static Exception Innermost(Exception ex)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;
            return ex;
        }

        public void Dispose()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).Wait(1000);
            }
            catch (Exception) { }
            try { _socket.Dispose(); }
            catch (Exception) { }
        }
    }

    /// <summary>What OBS holds and what it is currently on.</summary>
    public sealed class ObsSnapshot
    {
        public List<string> Profiles = new List<string>();
        public List<string> SceneCollections = new List<string>();
        public List<string> Scenes = new List<string>();
        public string CurrentProfile = string.Empty;
        public string CurrentSceneCollection = string.Empty;
        public string CurrentScene = string.Empty;
    }

    /// <summary>What the app actually asks OBS to do, over <see cref="ObsWebSocket"/>.</summary>
    internal static class ObsStudio
    {
        // A scene collection change reloads every source in it, so it is the one step worth waiting on.
        static readonly TimeSpan SwitchBudget = TimeSpan.FromSeconds(5);
        static readonly TimeSpan CollectionSwitchBudget = TimeSpan.FromSeconds(10);

        static ObsWebSocket Open(TimeSpan waitFor, out string error)
        {
            UserAppSettings settings = Globals.Instance.Settings;
            int port = settings == null ? 4455 : settings.ObsWebSocketPort;
            string password = settings == null ? string.Empty : settings.ObsPassword;
            return ObsWebSocket.Open(port, password, waitFor, out error);
        }

        /// <summary>
        /// Switches profile, scene collection and scene, in that order; an empty name leaves that one
        /// alone. Anything already set to the wanted value is left untouched, which matters because a
        /// needless collection switch costs seconds and reloads every source in it.
        /// </summary>
        public static bool Apply(string profileName, string sceneCollectionName, string sceneName,
            int waitForObsSeconds, Action<string> log, out string error)
        {
            using (ObsWebSocket obs = Open(TimeSpan.FromSeconds(Math.Max(0, waitForObsSeconds)), out error))
            {
                if (obs == null)
                    return false;

                if (!string.IsNullOrWhiteSpace(profileName)
                    && !Switch(obs, "GetProfileList", "profiles", "currentProfileName", "SetCurrentProfile", "profileName",
                        profileName, "profile", SwitchBudget, log, out error))
                    return false;

                if (!string.IsNullOrWhiteSpace(sceneCollectionName)
                    && !Switch(obs, "GetSceneCollectionList", "sceneCollections", "currentSceneCollectionName",
                        "SetCurrentSceneCollection", "sceneCollectionName", sceneCollectionName, "scene collection",
                        CollectionSwitchBudget, log, out error))
                    return false;

                if (!string.IsNullOrWhiteSpace(sceneName) && !SwitchScene(obs, sceneName, log, out error))
                    return false;

                error = null;
                return true;
            }
        }

        static bool SwitchScene(ObsWebSocket obs, string sceneName, Action<string> log, out string error)
        {
            // A collection switch rebuilds the scene list, so give the name the same grace.
            string exact = null;
            WaitUntil(() => (exact = SceneNamed(obs, sceneName)) != null, CollectionSwitchBudget);
            if (exact == null)
            {
                error = "OBS has no scene named '" + sceneName + "'";
                return false;
            }
            JObject scenes;
            string ignored;
            obs.Request("GetSceneList", null, out scenes, out ignored);
            if (scenes != null && Same((string)scenes["currentProgramSceneName"], exact))
            {
                error = null;
                return true;
            }
            log("Switching OBS to scene " + exact);
            JObject data = new JObject();
            data["sceneName"] = exact;
            return obs.Request("SetCurrentProgramScene", data, out error);
        }

        static bool Switch(ObsWebSocket obs, string listRequest, string listProperty, string currentProperty,
            string setRequest, string setProperty, string wanted, string what, TimeSpan budget, Action<string> log,
            out string error)
        {
            JObject list;
            if (!obs.Request(listRequest, null, out list, out error))
                return false;
            string exact = list == null ? null : Exact(list[listProperty], wanted);
            if (exact == null)
            {
                error = "OBS has no " + what + " named '" + wanted + "'";
                return false;
            }
            if (Same((string)list[currentProperty], exact))
                return true;

            log("Switching OBS to " + what + " " + exact);
            JObject data = new JObject();
            data[setProperty] = exact;
            if (!obs.Request(setRequest, data, out error))
                return false;

            // The request returns as soon as OBS accepts it, not when the switch has happened.
            if (!WaitUntil(() => IsCurrent(obs, listRequest, currentProperty, exact), budget))
            {
                error = "OBS did not switch to " + what + " '" + exact + "' in time";
                return false;
            }
            return true;
        }

        static bool IsCurrent(ObsWebSocket obs, string listRequest, string currentProperty, string wanted)
        {
            JObject list;
            string error;
            // Requests can fail while OBS reloads; that just means "not yet".
            return obs.Request(listRequest, null, out list, out error)
                && list != null && Same((string)list[currentProperty], wanted);
        }

        static string SceneNamed(ObsWebSocket obs, string wanted)
        {
            JObject list;
            string error;
            if (!obs.Request("GetSceneList", null, out list, out error) || list == null)
                return null;
            JArray scenes = list["scenes"] as JArray;
            if (scenes == null)
                return null;
            foreach (JToken scene in scenes)
            {
                string name = (string)scene["sceneName"];
                if (Same(name, wanted))
                    return name;
            }
            return null;
        }

        /// <summary>
        /// What OBS has and what it is on right now, for the drop-downs in the action. Does not wait
        /// for OBS to start. Null with a reason in <paramref name="error"/> when OBS is not there.
        /// </summary>
        public static ObsSnapshot Read(out string error)
        {
            using (ObsWebSocket obs = Open(TimeSpan.Zero, out error))
            {
                if (obs == null)
                    return null;
                ObsSnapshot snapshot = new ObsSnapshot();
                JObject response;
                if (obs.Request("GetProfileList", null, out response, out error) && response != null)
                {
                    snapshot.Profiles.AddRange(Names(response["profiles"]));
                    snapshot.CurrentProfile = (string)response["currentProfileName"] ?? string.Empty;
                }
                if (obs.Request("GetSceneCollectionList", null, out response, out error) && response != null)
                {
                    snapshot.SceneCollections.AddRange(Names(response["sceneCollections"]));
                    snapshot.CurrentSceneCollection = (string)response["currentSceneCollectionName"] ?? string.Empty;
                }
                if (obs.Request("GetSceneList", null, out response, out error) && response != null && response["scenes"] != null)
                {
                    snapshot.Scenes.AddRange(response["scenes"].Select(s => (string)s["sceneName"]).Where(n => n != null));
                    snapshot.CurrentScene = (string)response["currentProgramSceneName"] ?? string.Empty;
                }
                error = null;
                return snapshot;
            }
        }

        /// <summary>For the Test button in Settings.</summary>
        public static bool Test(out string message)
        {
            string error;
            using (ObsWebSocket obs = Open(TimeSpan.Zero, out error))
            {
                if (obs == null)
                {
                    message = error;
                    return false;
                }
                JObject version;
                if (!obs.Request("GetVersion", null, out version, out error))
                {
                    message = error;
                    return false;
                }
                JObject scenes;
                string ignored;
                obs.Request("GetSceneList", null, out scenes, out ignored);
                message = "Connected to OBS " + (version == null ? "?" : (string)version["obsVersion"]);
                if (scenes != null)
                    message += ", scene '" + (string)scenes["currentProgramSceneName"] + "'";
                return true;
            }
        }

        static IEnumerable<string> Names(JToken array)
        {
            return array == null ? new string[0] : array.Select(t => (string)t).Where(n => n != null);
        }

        static string Exact(JToken names, string wanted)
        {
            return Names(names).FirstOrDefault(n => Same(n, wanted));
        }

        static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static bool WaitUntil(Func<bool> done, TimeSpan budget)
        {
            DateTime deadline = DateTime.UtcNow + budget;
            while (DateTime.UtcNow < deadline)
            {
                if (done())
                    return true;
                Thread.Sleep(200);
            }
            return done();
        }
    }

    /// <summary>
    /// DPAPI for the one credential this app stores. Settings files get copied between machines, so a
    /// value that cannot be decrypted here is treated as "not set" rather than as an error.
    /// </summary>
    internal static class ProtectedText
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("arzGUI.obs-websocket");

        public static string Protect(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            try
            {
                return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy,
                    DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText))
                return string.Empty;
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy,
                    DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                // Written by another Windows account or machine - the user re-enters it.
                return string.Empty;
            }
        }
    }
}
