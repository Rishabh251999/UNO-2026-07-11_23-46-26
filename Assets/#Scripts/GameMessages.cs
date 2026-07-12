using Mirror;
using System;

namespace UNO
{
    /// <summary>
    /// Message sent from client to server for room operations
    /// </summary>
    public struct ServerRoomMessage : NetworkMessage
    {
        public ServerRoomOperation serverRoomOperation;
        public Guid roomCode;
    }

    /// <summary>
    /// Message sent from server to client
    /// </summary>
    public struct ClientRoomMessage : NetworkMessage
    {
        public ClientRoomOperation clientRoomOperation;
        public Guid roomCode;
        public RoomInfo[] roomInfo;
        public PlayerRoomInfo[] playerInfo;
        public string errorMessage;
    }

    /// <summary>
    /// Information about a room
    /// </summary>
    [Serializable]
    public struct RoomInfo
    {
        public Guid roomCode;
        public int playerCount;
        public int maxPlayers;
        public bool isStarted;
    }

    /// <summary>
    /// Information about a player in a room
    /// </summary>
    [Serializable]
    public struct PlayerRoomInfo
    {
        public int playerId;
        public bool isReady;
        public Guid roomCode;
        public string playerName;
    }

    /// <summary>
    /// Operations the server can perform on rooms
    /// </summary>
    public enum ServerRoomOperation : byte
    {
        None,
        Create,
        Join,
        Leave,
        List,
        Start,
        Ready,
        Cancel
    }

    /// <summary>
    /// Operations the client receives about rooms
    /// </summary>
    public enum ClientRoomOperation : byte
    {
        None,
        Created,
        Joined,
        Left,
        Cancelled,
        UpdateRoom,
        ListUpdated,
        Started,
        Error
    }
}