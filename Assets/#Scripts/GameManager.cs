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
        /// Event invoked when a player disconnects
        /// </summary>
        public event Action<NetworkConnectionToClient> OnPlayerDisconnect;

        /// <summary>
        /// Cross-reference of client that created the corresponding room
        /// </summary>
        internal readonly Dictionary<NetworkConnectionToClient, Guid> playerRooms =
            new();

        /// <summary>
        /// Open rooms that are available for joining
        /// </summary>
        internal readonly Dictionary<Guid, RoomInfo> openRooms =
            new();

        /// <summary>
        /// Network connections of all players in a room
        /// </summary>
        internal readonly Dictionary<Guid, HashSet<NetworkConnectionToClient>> roomConnections =
            new();

        /// <summary>
        /// Player information by Network Connection
        /// </summary>
        internal readonly Dictionary<NetworkConnectionToClient, PlayerRoomInfo> playerInfos =
            new();

        /// <summary>
        /// Network connections that haven't joined a room yet
        /// </summary>
        internal readonly List<NetworkConnectionToClient> waitingConnections =
            new();

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

        [SerializeField] private RoomManager _roomManager;

        [SerializeField] private int maxPlayersPerRoom = 4;
        private int playerIndex = 1;

        [Header("GUI References")]
        [SerializeField] private RoomGUI matchPrefab;

        [SerializeField] private GameObject roomView;
        [SerializeField] private Transform matchList;
        [SerializeField] private GameObject lobbyView;
        [SerializeField] private GameObject matchControllerPrefab;

        [SerializeField] private ToggleGroup toggleGroup;
        [SerializeField] private Button createButton;
        [SerializeField] private Button joinButton;

        #region Initialization

        internal void InitializeData()
        {
            playerRooms.Clear();
            openRooms.Clear();
            roomConnections.Clear();
            waitingConnections.Clear();
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
        }

        [ServerCallback]
        internal void OnServerReady(NetworkConnectionToClient conn)
        {
            waitingConnections.Add(conn);
            playerInfos.Add(conn, new PlayerRoomInfo
            {
                playerId = playerIndex,
                isReady = false
            });
            playerIndex++;

            SendRoomList();
        }

        [ServerCallback]
        internal IEnumerator OnServerDisconnect(NetworkConnectionToClient conn)
        {
            // Invoke event so RoomControllers can clean up
            OnPlayerDisconnect?.Invoke(conn);

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
                        PlayerRoomInfo playerInfo = playerInfos[playerConn];
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
            InitializeData();
            ShowLobbyView();
            createButton.gameObject.SetActive(true);
            joinButton.gameObject.SetActive(true);
            NetworkClient.RegisterHandler<ClientRoomMessage>(OnClientRoomMessage);
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

            PlayerRoomInfo playerInfo = playerInfos[conn];
            playerInfo.isReady = false;
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
            Debug.Log($"RoomManager: Room {newRoomCode} created");
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

            PlayerRoomInfo playerInfo = playerInfos[conn];
            playerInfo.isReady = false;
            playerInfo.roomCode = roomCode;
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
            SendRoomList();
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

                    UpdateRoomList();
                    break;

                case ClientRoomOperation.UpdateRoom:
                    _roomManager.RefreshRoomPlayers(msg.playerInfo);
                    break;

                case ClientRoomOperation.Error:
                    Debug.LogError($"Room error: {msg.errorMessage}");
                    break;
            }
        }

        #endregion

        #region Button Callbacks (UI)

        /// <summary>
        /// Called from Create Button
        /// </summary>
        [ClientCallback]
        public void RequestCreateRoom()
        {
            NetworkClient.Send(new ServerRoomMessage { serverRoomOperation = ServerRoomOperation.Create });
        }

        /// <summary>
        /// Called from Join Button
        /// </summary>
        [ClientCallback]
        public void RequestJoinRoom()
        {
            if (selectedRoom == Guid.Empty)
            {
                Debug.LogWarning("No room selected");
                return;
            }

            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Join,
                roomCode = selectedRoom
            });
        }

        /// <summary>
        /// Called from Leave Button
        /// </summary>
        [ClientCallback]
        public void RequestLeaveRoom()
        {
            if (localJoinedRoom == Guid.Empty)
            {
                Debug.LogWarning("Not in a room");
                return;
            }

            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Leave
            });
        }

        [ClientCallback]
        public void RequestCancelRoom()
        {
            if (localPlayerRoom == Guid.Empty)
            {
                Debug.LogWarning("Not the room owner");
                return;
            }
            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Cancel
            });
        }

        /// <summary>
        /// Called from Ready Button
        /// </summary>
        [ClientCallback]
        public void RequestReadyChange()
        {
            if (localPlayerRoom == Guid.Empty && localJoinedRoom == Guid.Empty)
            {
                Debug.LogWarning("Not in a room");
                return;
            }

            var roomCode = localPlayerRoom != Guid.Empty ? localPlayerRoom : localJoinedRoom;

            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Ready,
                roomCode = roomCode
            });
        }

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
                RoomInfo roomInfo = openRooms[roomId];
                joinButton.interactable = roomInfo.playerCount < roomInfo.maxPlayers;
            }
        }

        /// <summary>
        /// Show lobby view (room list)
        /// </summary>
        [ClientCallback]
        public void ShowLobbyView()
        {
            lobbyView.SetActive(true);
            roomView.SetActive(false);

            foreach (Transform item in matchList)
            {
                if(item.TryGetComponent<RoomGUI>(out RoomGUI roomGUI))
                {
                    if(roomGUI.GetRoomCode() == selectedRoom)
                    {
                        roomGUI.TryGetComponent<Toggle>(out var toggle);
                        toggle.isOn = true;
                    }
                }
            }
        }

        /// <summary>
        /// Show room view (inside a room)
        /// </summary>
        [ClientCallback]
        public void ShowRoomView()
        {
            lobbyView.SetActive(false);
            roomView.SetActive(true);
        }

        #endregion

        #region UI Update Methods

        /// <summary>
        /// Update the room list UI with available rooms
        /// </summary>
        [ClientCallback]
        public void UpdateRoomList()
        {
            // Clear existing room list UI
            foreach (Transform child in matchList.transform)
                Destroy(child.gameObject);

            // Create UI elements for each room
            foreach (var roomInfo in openRooms.Values)
            {
                var roomUIElement = Instantiate(matchPrefab, matchList.transform);
                roomUIElement.transform.SetParent(matchList.transform, false);
                roomUIElement.SetRoomInfo(roomInfo);

                var toggle = roomUIElement.GetComponent<Toggle>();
                toggle.group = toggleGroup;

                if (roomInfo.roomCode == selectedRoom)
                    toggle.isOn = true;
            }
        }

        public void OnRoomCreated(Guid roomId)
        {
            localPlayerRoom = roomId;
            ShowRoomView();
            Debug.Log($"Room created: {roomId}");
        }

        [ClientCallback]
        public void OnRoomJoined(Guid roomId)
        {
            localJoinedRoom = roomId;
            selectedRoom = Guid.Empty;
            ShowRoomView();
            Debug.Log($"Joined room: {roomId}");
        }


        public void OnRoomLeft()
        {
            localPlayerRoom = Guid.Empty;
            localJoinedRoom = Guid.Empty;
            ShowLobbyView();
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

        #endregion
    }
}