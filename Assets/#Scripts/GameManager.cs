using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    public class GameManager : MonoBehaviour
    {
        /// <summary>
        /// Cross-reference of client that created the corresponding room
        /// </summary>
        internal readonly Dictionary<NetworkConnectionToClient, Guid> playerRooms = new();

        /// <summary>
        /// Open rooms that are available for joining
        /// </summary>
        internal readonly Dictionary<Guid, RoomInfo> openRooms = new();

        /// <summary>
        /// Network connections of all players in a room
        /// </summary>
        internal readonly Dictionary<Guid, HashSet<NetworkConnectionToClient>> roomConnections = new();

        /// <summary>
        /// Player information by Network Connection
        /// </summary>
        internal readonly Dictionary<NetworkConnectionToClient, PlayerRoomInfo> playerInfos = new();

        private readonly Dictionary<Guid, UnoDeck> roomDecks = new();

        private readonly Dictionary<Guid, UnoGameController> matchControllers = new();

        /// <summary>
        /// Network connections that haven't joined a room yet
        /// </summary>
        internal readonly List<NetworkConnectionToClient> waitingConnections = new();

        /// <summary>
        /// Room code the local player created
        /// </summary>
        internal Guid localPlayerRoom = Guid.Empty;

        /// <summary>
        /// Room code the local player joined
        /// </summary>
        internal Guid localJoinedRoom = Guid.Empty;

        /// <summary>
        /// Room code selected in the room list
        /// </summary>
        internal Guid selectedRoom = Guid.Empty;

        [SerializeField] private LobbyManager _lobbyManager;
        [SerializeField] private RoomManager _roomManager;

        [SerializeField] private int maxPlayersPerRoom = 4;
        private int playerIndex = 1;

        [Header("GUI References")]
        [SerializeField] private UnoGameController matchControllerPrefab;

        [SerializeField] private Button joinButton;

        #region Initialization

        internal void InitializeData()
        {
            playerRooms.Clear();
            openRooms.Clear();
            roomConnections.Clear();
            waitingConnections.Clear();
            roomDecks.Clear();
            matchControllers.Clear();
            playerIndex = 1;
            localPlayerRoom = Guid.Empty;
            localJoinedRoom = Guid.Empty;
            selectedRoom = Guid.Empty;
        }

        void ResetRoomManager()
        {
            InitializeData();
            gameObject.SetActive(false);
        }

        #endregion

        #region Server Callbacks

        [ServerCallback]
        internal void OnStartServer()
        {
            InitializeData();
            NetworkServer.RegisterHandler<ServerRoomMessage>(OnServerRoomMessage);
            NetworkServer.RegisterHandler<ServerDeckMessage>(OnServerDeckMessage);
        }

        [ServerCallback]
        internal void OnServerReady(NetworkConnectionToClient conn)
        {
            waitingConnections.Add(conn);
            playerInfos.Add(conn, new()
            {
                playerId = playerIndex,
                isReady = false,
                playerName = GenerateRandomPlayerName()
            });
            playerIndex++;

            SendRoomList();
        }

        [ServerCallback]
        internal IEnumerator OnServerDisconnect(NetworkConnectionToClient conn)
        {
            // If player created a room, remove it
            if (playerRooms.TryGetValue(conn, out var roomCode))
            {
                playerRooms.Remove(conn);
                openRooms.Remove(roomCode);

                // Notify all players in that room
                if (roomConnections.TryGetValue(roomCode, out var connections))
                {
                    foreach (NetworkConnectionToClient playerConn in connections)
                    {
                        var playerInfo = playerInfos[playerConn];
                        playerInfo.isReady = false;
                        playerInfo.roomCode = Guid.Empty;
                        playerInfos[playerConn] = playerInfo;
                        playerConn.Send(new ClientRoomMessage
                        {
                            clientRoomOperation = ClientRoomOperation.Left
                        });
                    }
                }
            }

            // Remove player from all rooms
            foreach (var kvp in roomConnections)
                kvp.Value.Remove(conn);

            // Update room if player was in one
            if (playerInfos.TryGetValue(conn, out PlayerRoomInfo info))
            {
                if (openRooms.TryGetValue(info.roomCode, out RoomInfo roomInfo))
                {
                    roomInfo.playerCount--;
                    openRooms[info.roomCode] = roomInfo;
                }

                // Notify remaining players
                if (roomConnections.TryGetValue(info.roomCode, out var connections2))
                {
                    PlayerRoomInfo[] playerInfo = connections2
                        .Where(c => playerInfos.ContainsKey(c))
                        .Select(playerConn => playerInfos[playerConn])
                        .ToArray();

                    foreach (NetworkConnectionToClient playerConn in connections2)
                        if (playerConn != conn)
                            playerConn.Send(new ClientRoomMessage
                            {
                                clientRoomOperation = ClientRoomOperation.ListUpdated,
                                playerInfo = playerInfo
                            });
                }
            }

            playerInfos.Remove(conn);
            waitingConnections.Remove(conn);
            SendRoomList();

            yield return null;
        }

        [ServerCallback]
        internal void OnStopServer()
        {
            ResetRoomManager();
        }

        #endregion

        #region Client Callbacks

        [ClientCallback]
        internal void OnStartClient()
        {
            Card.PopulateCardSprites();

            InitializeData();

            UIManager.Instance.SetState(ScreenType.Lobby);

            _lobbyManager.UpdateRoomList(openRooms);

            NetworkClient.RegisterHandler<ClientRoomMessage>(OnClientRoomMessage);
            NetworkClient.RegisterHandler<ClientDeckMessage>(OnClientDeckMessage);
        }

        [ClientCallback]
        internal void OnClientDisconnect()
        {
            InitializeData();
        }

        [ClientCallback]
        internal void OnStopClient()
        {
            ResetRoomManager();
        }

        #endregion

        #region Server Message Handler

        [ServerCallback]
        void OnServerRoomMessage(NetworkConnectionToClient conn, ServerRoomMessage msg)
        {
            switch (msg.serverRoomOperation)
            {
                case ServerRoomOperation.Create:
                    OnServerCreateRoom(conn);
                    break;
                case ServerRoomOperation.Join:
                    OnServerJoinRoom(conn, msg.roomCode);
                    break;
                case ServerRoomOperation.Leave:
                    OnServerLeaveRoom(conn);
                    break;
                case ServerRoomOperation.Cancel:
                    OnServerCancelRoom(conn);
                    break;
                case ServerRoomOperation.List:
                    SendRoomList(conn);
                    break;
                case ServerRoomOperation.Ready:
                    OnServerPlayerReady(conn, msg.roomCode);
                    break;
                case ServerRoomOperation.Start:
                    OnServerStartGame(conn);
                    break;
            }
        }

        [ServerCallback]
        private bool TryGetMatchController(NetworkConnectionToClient conn, out UnoGameController controller)
        {
            controller = null;

            // Find which room this connection's player is in via NetworkMatch
            if (conn.identity == null) return false;

            if (!conn.identity.TryGetComponent<NetworkMatch>(out var networkMatch)) 
                return false;

            Guid roomCode = networkMatch.matchId;
            return matchControllers.TryGetValue(roomCode, out controller);
        }

        [ServerCallback]
        void OnServerDeckMessage(NetworkConnectionToClient conn, ServerDeckMessage msg)
        {
            if (!TryGetMatchController(conn, out var controller))
            {
                Debug.LogWarning("[Server] Could not find match controller for connection.");
                return;
            }

            if (msg.serverDeckOperation is ServerDeckOperation.QuitMatch)
            {
                HandleQuitMatch(conn, controller);
                return;
            }

            if (!controller.IsCurrentPlayer(conn))
            {
                Debug.LogWarning($"[Server] Player {conn.identity.netId} acted out of turn!");
                return;
            }

            switch (msg.serverDeckOperation)
            {
                case ServerDeckOperation.PlayCard:
                    controller.HandlePlayerCard(conn, msg.Card, msg.chosenWildColor);
                    break;

                case ServerDeckOperation.DrawCard:
                    controller.HandleDrawCard(conn);
                    break;

                case ServerDeckOperation.PassTurn:
                    controller.HandlePassTurn(conn); // Draw card first, then pass turn
                    break;
            }
        }

        [ServerCallback]
        void OnServerCreateRoom(NetworkConnectionToClient conn)
        {
            if (playerRooms.ContainsKey(conn)) return;

            var newRoomCode = Guid.NewGuid();
            roomConnections.Add(newRoomCode, new HashSet<NetworkConnectionToClient> { conn });
            playerRooms.Add(conn, newRoomCode);
            openRooms.Add(newRoomCode, new()
            {
                roomCode = newRoomCode,
                maxPlayers = maxPlayersPerRoom,
                playerCount = 1,
                isStarted = false
            });

            var playerInfo = playerInfos[conn];
            playerInfo.isReady = false;
            playerInfo.isOwner = true;
            playerInfo.roomCode = newRoomCode;
            playerInfos[conn] = playerInfo;

            PlayerRoomInfo[] info = roomConnections[newRoomCode]
                .Select(playerConn => playerInfos[playerConn])
                .ToArray();

            conn.Send(new ClientRoomMessage
            {
                clientRoomOperation = ClientRoomOperation.Created,
                roomCode = newRoomCode,
                playerInfo = info
            });

            SendRoomList();
        }

        [ServerCallback]
        void OnServerJoinRoom(NetworkConnectionToClient conn, Guid roomCode)
        {
            if (!roomConnections.ContainsKey(roomCode) || !openRooms.ContainsKey(roomCode))
            {
                conn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.Error,
                    errorMessage = $"Room {roomCode} not found"
                });
                return;
            }

            RoomInfo roomInfo = openRooms[roomCode];
            if (roomInfo.playerCount >= roomInfo.maxPlayers)
            {
                conn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.Error,
                    errorMessage = $"Room {roomCode} is full"
                });
                return;
            }

            roomInfo.playerCount++;
            openRooms[roomCode] = roomInfo;
            roomConnections[roomCode].Add(conn);

            var playerInfo = playerInfos[conn];
            playerInfo.isReady = false;
            playerInfo.roomCode = roomCode;
            playerInfo.isOwner = false;
            playerInfos[conn] = playerInfo;

            PlayerRoomInfo[] info = roomConnections[roomCode]
                .Select(playerConn => playerInfos[playerConn])
                .ToArray();

            SendRoomList();

            conn.Send(new ClientRoomMessage
            {
                clientRoomOperation = ClientRoomOperation.Joined,
                roomCode = roomCode,
                playerInfo = info
            });

            foreach (NetworkConnectionToClient playerConn in roomConnections[roomCode])
                playerConn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.UpdateRoom,
                    playerInfo = info
                });

            Debug.Log($"RoomManager: Player joined room {roomCode}");
        }

        [ServerCallback]
        void OnServerLeaveRoom(NetworkConnectionToClient conn)
        {
            if (!playerInfos.TryGetValue(conn, out PlayerRoomInfo playerInfo))
                return;

            var roomCode = playerInfo.roomCode;

            if (!roomConnections.ContainsKey(roomCode))
                return;

            roomConnections[roomCode].Remove(conn);
            playerInfo.isReady = false;
            playerInfo.roomCode = Guid.Empty;
            playerInfos[conn] = playerInfo;

            if (roomConnections[roomCode].Count == 0)
            {
                roomConnections.Remove(roomCode);
                openRooms.Remove(roomCode);
                Debug.Log($"RoomManager: Room {roomCode} closed (empty)");
            }
            else
            {
                RoomInfo roomInfo = openRooms[roomCode];
                roomInfo.playerCount = roomConnections[roomCode].Count;
                openRooms[roomCode] = roomInfo;

                PlayerRoomInfo[] playerRoomInfo = roomConnections[roomCode]
                    .Select(playerConn => playerInfos[playerConn])
                    .ToArray();

                foreach (NetworkConnectionToClient playerConn in roomConnections[roomCode])
                    playerConn.Send(new ClientRoomMessage
                    {
                        clientRoomOperation = ClientRoomOperation.UpdateRoom,
                        playerInfo = playerRoomInfo
                    });
            }

            SendRoomList();
        }


        [ServerCallback]
        void OnServerCancelRoom(NetworkConnectionToClient conn)
        {
            if (!playerRooms.ContainsKey(conn)) return;

            conn.Send(new ClientRoomMessage
            {
                clientRoomOperation = ClientRoomOperation.Cancelled,
            });

            if (playerRooms.TryGetValue(conn, out var roomCode))
            {
                playerRooms.Remove(conn);
                openRooms.Remove(roomCode);

                foreach (var item in roomConnections[roomCode])
                {
                    var playerInfo = playerInfos[item];
                    playerInfo.isReady = false;
                    playerInfo.roomCode = Guid.Empty;
                    item.Send(new ClientRoomMessage
                    {
                        clientRoomOperation = ClientRoomOperation.Left
                    });
                }

                SendRoomList();
            }
        }

        [ServerCallback]
        void OnServerPlayerReady(NetworkConnectionToClient conn, Guid roomCode)
        {
            var playerInfo = playerInfos[conn];

            playerInfo.isReady = !playerInfo.isReady;
            playerInfos[conn] = playerInfo;

            HashSet<NetworkConnectionToClient> connections = roomConnections[roomCode];
            var info = connections.Select(playerConn => playerInfos[playerConn]).ToArray();

            foreach (var item in roomConnections[roomCode])
            {
                item.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.UpdateRoom,
                    playerInfo = info
                });
            }
        }

        [ServerCallback]
        private void OnServerStartGame(NetworkConnectionToClient conn)
        {
            if(!playerRooms.TryGetValue(conn, out var roomCode))
                return;

            var matchController = Instantiate(matchControllerPrefab);
            if (matchController.TryGetComponent<NetworkMatch>(out var networkMatch))
            {
                networkMatch.matchId = roomCode;
            }
            NetworkServer.Spawn(matchController.gameObject);
            matchControllers[roomCode] = matchController;

            UnoDeck deck = new();
            deck.BuildDeck();
            roomDecks[roomCode] = deck;

            foreach (NetworkConnectionToClient playerConn in roomConnections[roomCode])
            {
                playerConn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.Started,
                    roomCode = roomCode
                });

                var player = Instantiate(NetworkManager.singleton.playerPrefab);
                if (player.TryGetComponent<NetworkMatch>(out var playerNetworkMatch))
                {
                    playerNetworkMatch.matchId = roomCode;
                }
                NetworkServer.AddPlayerForConnection(playerConn, player);

                matchController.AddPlayer(playerConn, playerInfos[playerConn]);

                PlayerRoomInfo playerInfo = playerInfos[playerConn];
                playerInfo.isReady = false;
                playerInfos[playerConn] = playerInfo;
            }

            matchController.StartGame(deck);

            playerRooms.Remove(conn);
            openRooms.Remove(roomCode);
            roomConnections.Remove(roomCode);

            SendRoomList();
        }

        [ServerCallback]
        private void HandleQuitMatch(NetworkConnectionToClient conn, UnoGameController controller)
        {
            var isOwner = playerInfos.TryGetValue(conn, out var playerInfo) && playerInfo.isOwner;

            if (isOwner)
                EndMatchForRoom(controller);

            else
                controller.HandlePlayerQuit(conn);
        }

        [ServerCallback]
        private void EndMatchForRoom(UnoGameController controller)
        {
            if (!controller.TryGetComponent<NetworkMatch>(out var networkMatch))
                return;

            Guid roomCode = networkMatch.matchId;

            controller.EndMatch(); 

            matchControllers.Remove(roomCode);
            roomDecks.Remove(roomCode);

            if (controller != null)
                NetworkServer.Destroy(controller.gameObject);
        }

        #endregion

        #region Client Message Handler

        [ClientCallback]
        void OnClientRoomMessage(ClientRoomMessage msg)
        {
            switch (msg.clientRoomOperation)
            {
                case ClientRoomOperation.Created:
                    OnRoomCreated(msg.roomCode);

                    _roomManager.RefreshRoomPlayers(msg.playerInfo);
                    _roomManager.SetRoomCode(msg.roomCode);
                    _roomManager.SetOwner(true);
                    break;

                case ClientRoomOperation.Joined:
                    OnRoomJoined(msg.roomCode);

                    _roomManager.RefreshRoomPlayers(msg.playerInfo);
                    _roomManager.SetRoomCode(msg.roomCode);
                    _roomManager.SetOwner(false);
                    break;

                case ClientRoomOperation.Left:
                    OnRoomLeft();
                    break;

                case ClientRoomOperation.ListUpdated:
                    openRooms.Clear();

                    foreach (var item in msg.roomInfo)
                        openRooms.Add(item.roomCode, item);

                    _lobbyManager.UpdateRoomList(openRooms);
                    break;

                case ClientRoomOperation.UpdateRoom:
                    _roomManager.RefreshRoomPlayers(msg.playerInfo);
                    break;

                case ClientRoomOperation.Started:
                    UIManager.Instance.SetState(ScreenType.Game);
                    break;

                case ClientRoomOperation.MatchEndedByOwner:
                    OnRoomLeft();
                    break;

                case ClientRoomOperation.Error:
                    Debug.LogError($"Room error: {msg.errorMessage}");
                    break;
            }
        }

        [ClientCallback]
        void OnClientDeckMessage(ClientDeckMessage msg)
        {
            if(UnoGameController.Instance is not { } gc)
                return;

            switch (msg.clientDeckOperation)
            {
                case ClientDeckOperation.CardDealt:
                    gc.ShowDealtCards(msg.Cards, true);
                    break;

                case ClientDeckOperation.CardPlayed:
                    gc.RefreshHandInteractability(false);
                    break;

                case ClientDeckOperation.CardDrawn:
                    gc.ShowDealtCards(msg.Cards, false);  
                    gc.OnDrawnCardReceived(msg.CanPlayDrawnCard, msg.Cards[0]);
                    break;

                case ClientDeckOperation.DeckReshuffled:
                    break;

                case ClientDeckOperation.StackedDraw:
                    break;

                case ClientDeckOperation.Error:
                    Debug.LogError($"[Deck] Error: {msg.errorMessage}");
                    break;
            }
        }

        #endregion

        #region Button Callbacks (UI)

        /// <summary>
        /// Called when a room is selected in the list
        /// </summary>
        [ClientCallback]
        public void SelectRoom(Guid roomId)
        {
            if (roomId == Guid.Empty)
            {
                selectedRoom = Guid.Empty;
                joinButton.interactable = false;
            }
            else
            {
                if (!openRooms.ContainsKey(roomId))
                {
                    joinButton.interactable = false;
                    return;
                }

                selectedRoom = roomId;
                var roomInfo = openRooms[roomId];
                joinButton.interactable = roomInfo.playerCount < roomInfo.maxPlayers;
            }
        }

        #endregion

        #region UI Update Methods

        public void OnRoomCreated(Guid roomId)
        {
            localPlayerRoom = roomId;
            UIManager.Instance.SetState(ScreenType.Room);
        }

        [ClientCallback]
        public void OnRoomJoined(Guid roomId)
        {
            localJoinedRoom = roomId;
            selectedRoom = Guid.Empty;
            UIManager.Instance.SetState(ScreenType.Room);
        }


        public void OnRoomLeft()
        {
            localPlayerRoom = Guid.Empty;
            localJoinedRoom = Guid.Empty;

            UIManager.Instance.SetState(ScreenType.Lobby);

            _lobbyManager.UpdateRoomList(openRooms);

            Debug.Log("Left room");
        }

        #endregion

        #region Helper Methods

        [ServerCallback]
        void SendRoomList(NetworkConnectionToClient conn = null)
        {
            if (conn != null)
            { 
                conn.Send(new ClientRoomMessage
                {
                    clientRoomOperation = ClientRoomOperation.ListUpdated,
                    roomInfo = openRooms.Values.ToArray()
                });
            }

            else
            {
                foreach (var item in waitingConnections)
                {
                    item.Send(new ClientRoomMessage
                    {
                        clientRoomOperation = ClientRoomOperation.ListUpdated,
                        roomInfo = openRooms.Values.ToArray()
                    });
                }
            }
        }

        private string GenerateRandomPlayerName()
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            Span<char> buffer = stackalloc char[3];

            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = letters[UnityEngine.Random.Range(0, letters.Length)];

            return new string(buffer);
        }

        #endregion
    }
}