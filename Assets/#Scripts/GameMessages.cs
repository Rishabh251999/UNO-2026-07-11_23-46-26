using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UNO
{
    #region Network Messages

    /// <summary>
    /// Message sent from client to server for room operations
    /// </summary>
    public struct ServerRoomMessage : NetworkMessage
    {
        public ServerRoomOperation serverRoomOperation;
        public Guid roomCode;
    }

    public struct ServerDeckMessage : NetworkMessage
    {
        public ServerDeckOperation serverDeckOperation;
        public Guid RoomCode;
        public UnoCard Card;
        public CardColor chosenWildColor;
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

    public struct ClientDeckMessage : NetworkMessage
    {
        public ClientDeckOperation clientDeckOperation;
        public Guid RoomCode;
        public UnoCard[] Cards;
        public UnoCard TopDiscardCard;
        public int DrawPileCount;
        public string errorMessage;
        public bool CanPlayDrawnCard;
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
        public int cardCount;
        public bool isReady;
        public bool isOwner;
        public Guid roomCode;
        public string playerName;
    }

    [Serializable]
    public struct PlayerGameInfo
    {
        public bool isOwner;

        public int cardCount;
        public uint connectionId;
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

    public enum ServerDeckOperation : byte
    {
        None = 0,
        DrawCard = 1,  
        PlayCard = 2, 
        PassTurn = 3,
        QuitMatch = 4,   // NEW
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

    public enum ClientDeckOperation : byte
    {
        None = 0,
        DeckCreated = 1,
        CardDealt = 2,
        CardPlayed = 3,
        CardDrawn = 4,
        DeckReshuffled = 5,
        Error = 6,
        StackedDraw = 7,
        PlayerQuit = 8,
    }

    #endregion

    #region Card

    public struct CardData
    {
        public string name;
        public byte value;
        public Texture sprite;
    }

    public struct PlayerCardData
    {
        public byte cardId;

        [NonSerialized]
        public GameObject clientCardObject;
    }

    public class CustomSyncDictionary<TKey, TValue> : Mirror.SyncIDictionary<TKey, TValue>
    {
        public CustomSyncDictionary(IDictionary<TKey, TValue> objects) : base(objects) 
        { 

        }

        public bool SetLocalValue(TKey key, TValue value)
        {
            if (ContainsKey(key))
            {
                objects[key] = value;
                return true;
            }
            return false;
        }
    }

    public enum CardColor : byte
    {
        None = 0,
        Red = 1,
        Yellow = 2,
        Green = 3,
        Blue = 4,
    }

    public enum CardType : byte
    {
        Number = 0,
        Skip = 1,
        Reverse = 2,
        DrawTwo = 3,
        Wild = 4,
        WildDrawFour = 5
    }

    [Serializable]
    public struct UnoCard
    {
        public byte Id;

        public CardColor Color;

        public CardType Type;

        public byte FaceValue;

        public override readonly string ToString()
        {
            string colorStr = Color == CardColor.None ? "" : $"{Color} ";
            string typeStr = Type == CardType.Number
                ? FaceValue.ToString()
                : TypeToString(Type);
            return $"{colorStr}{typeStr}";
        }

        private readonly string TypeToString(CardType type) => type switch
        {
            CardType.Skip => "Skip",
            CardType.Reverse => "Reverse",
            CardType.DrawTwo => "Draw",
            CardType.Wild => "Wild",
            CardType.WildDrawFour => "WildDrawFour",
            _ => "Unknown"
        };
    }

    #endregion
}