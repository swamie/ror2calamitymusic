using System;
using System.Collections.Generic;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

namespace CalamityMusic.Game
{
    /// <summary>
    /// Keeps everyone on the same song when the host also runs the mod. Each game still detects
    /// fights on its own (everything it reads is networked); the host just tells modded clients
    /// which file it picked for each "context" (a boss theme, a stage, Boss Rush tier...), and
    /// clients play that file instead of their own random pick when they have it.
    ///
    /// Clients say hello first, and the host only ever messages connections that did, so players
    /// without the mod never receive a message type they don't know.
    /// </summary>
    internal sealed class MusicSync
    {
        private const short MsgHello = 23571; // client -> host: "I have the mod"
        private const short MsgPlay = 23572;  // host -> clients: "context\nrelative/path.ogg"
        private const float ChoiceLifetime = 20f;

        private struct Choice
        {
            public string Rel;
            public float Time;
        }

        private readonly HashSet<NetworkConnection> modded = new HashSet<NetworkConnection>();
        private readonly Dictionary<string, Choice> hostChoices = new Dictionary<string, Choice>();
        private readonly object gate = new object();
        private readonly Queue<KeyValuePair<string, string>> arrived = new Queue<KeyValuePair<string, string>>();
        private NetworkClient client;
        private bool serverHandlerRegistered;

        /// <summary>The host's pick arrived on a client: (context, relative path). Raised on the main thread from Poll.</summary>
        public event Action<string, string> HostChoiceArrived;

        public void Init()
        {
            NetworkManagerSystem.onStartServerGlobal += OnStartServer;
            NetworkManagerSystem.onStopServerGlobal += OnStopServer;
            NetworkManagerSystem.onServerDisconnectGlobal += OnServerDisconnect;
            NetworkManagerSystem.onStartClientGlobal += OnStartClient;
            NetworkManagerSystem.onStopClientGlobal += OnStopClient;
            NetworkManagerSystem.onClientConnectGlobal += OnClientConnect;
        }

        public void Shutdown()
        {
            NetworkManagerSystem.onStartServerGlobal -= OnStartServer;
            NetworkManagerSystem.onStopServerGlobal -= OnStopServer;
            NetworkManagerSystem.onServerDisconnectGlobal -= OnServerDisconnect;
            NetworkManagerSystem.onStartClientGlobal -= OnStartClient;
            NetworkManagerSystem.onStopClientGlobal -= OnStopClient;
            NetworkManagerSystem.onClientConnectGlobal -= OnClientConnect;
        }

        private static bool Enabled => ModConfig.MultiplayerSync.Value;

        /// <summary>True on a client (not the host) of a multiplayer game.</summary>
        public static bool IsRemoteClient => NetworkClient.active && !NetworkServer.active;

        // ------------------------------------------------------------------ host side

        private void OnStartServer()
        {
            try
            {
                lock (gate) modded.Clear();
                NetworkServer.RegisterHandler(MsgHello, OnHello);
                serverHandlerRegistered = true;
            }
            catch (Exception e)
            {
                Log.Warning("Music sync: could not register the server handler: " + e.Message);
            }
        }

        private void OnStopServer()
        {
            lock (gate) modded.Clear();
            if (!serverHandlerRegistered) return;
            try { NetworkServer.UnregisterHandler(MsgHello); } catch { /* server already torn down */ }
            serverHandlerRegistered = false;
        }

        private void OnServerDisconnect(NetworkConnection conn)
        {
            lock (gate) modded.Remove(conn);
        }

        private void OnHello(NetworkMessage msg)
        {
            if (msg?.conn == null) return;
            lock (gate) modded.Add(msg.conn);
            Log.Info("Music sync: a player with Calamity Music joined.");
        }

        /// <summary>Host: tells modded clients which song plays for <paramref name="context"/>.</summary>
        public void Announce(string context, string relPath)
        {
            if (!Enabled || !NetworkServer.active || string.IsNullOrEmpty(context) || string.IsNullOrEmpty(relPath)) return;
            NetworkConnection[] targets;
            lock (gate)
            {
                if (modded.Count == 0) return;
                targets = new NetworkConnection[modded.Count];
                modded.CopyTo(targets);
            }
            var message = new StringMessage(context + "\n" + relPath);
            foreach (var conn in targets)
            {
                try
                {
                    // The host's own client never says hello, so this is only ever remote players.
                    if (conn == null || !conn.isConnected) continue;
                    conn.Send(MsgPlay, message);
                }
                catch (Exception e)
                {
                    Log.Warning("Music sync: send failed: " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------------ client side

        private void OnStartClient(NetworkClient c)
        {
            client = c;
            lock (gate) hostChoices.Clear();
            try
            {
                c?.RegisterHandler(MsgPlay, OnPlay);
            }
            catch (Exception e)
            {
                Log.Warning("Music sync: could not register the client handler: " + e.Message);
            }
        }

        private void OnStopClient()
        {
            client = null;
            lock (gate) hostChoices.Clear();
        }

        private void OnClientConnect(NetworkConnection conn)
        {
            // The host's own client is local; only remote clients announce themselves.
            if (NetworkServer.active || conn == null) return;
            try
            {
                conn.Send(MsgHello, new StringMessage(CalamityMusicPlugin.Version));
            }
            catch (Exception e)
            {
                Log.Warning("Music sync: hello failed: " + e.Message);
            }
        }

        private void OnPlay(NetworkMessage msg)
        {
            if (NetworkServer.active) return;
            string text;
            try { text = msg.ReadMessage<StringMessage>()?.value; }
            catch { return; }
            if (string.IsNullOrEmpty(text)) return;
            int nl = text.IndexOf('\n');
            if (nl <= 0 || nl >= text.Length - 1) return;
            string context = text.Substring(0, nl), rel = text.Substring(nl + 1);
            lock (gate)
            {
                hostChoices[context] = new Choice { Rel = rel, Time = Time.unscaledTime };
                arrived.Enqueue(new KeyValuePair<string, string>(context, rel));
            }
        }

        /// <summary>Client: the host's recent pick for <paramref name="context"/>, if any.</summary>
        public bool TryGetHostChoice(string context, out string relPath)
        {
            relPath = null;
            if (!Enabled || !IsRemoteClient || context == null) return false;
            lock (gate)
            {
                if (!hostChoices.TryGetValue(context, out var c) || Time.unscaledTime - c.Time > ChoiceLifetime) return false;
                relPath = c.Rel;
                return true;
            }
        }

        /// <summary>Delivers host picks that arrived since the last frame.</summary>
        public void Poll()
        {
            while (true)
            {
                KeyValuePair<string, string> item;
                lock (gate)
                {
                    if (arrived.Count == 0) return;
                    item = arrived.Dequeue();
                }
                if (Enabled) HostChoiceArrived?.Invoke(item.Key, item.Value);
            }
        }
    }
}
