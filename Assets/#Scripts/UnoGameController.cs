using Mirror;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    [RequireComponent(typeof(NetworkMatch))]
    public class UnoGameController : NetworkBehaviour
    {
        public static UnoGameController Instance { get; private set; }

        [Header("GUI References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private GamePlayerGUI _playerGUIPrefab;

        [SerializeField] private Transform _handContainer;
        [SerializeField] private Transform _playersContainer;

        [SerializeField] private Card _cardPrefab;

        [SerializeField] private Button _cardDrawButton;
        [SerializeField] private Button _passTurnButton;
        [SerializeField] private Image _topDiscardImage;
        [SerializeField] private TextMeshProUGUI _drawPileCountText;


        private readonly List<uint> _turnOrder = new();
        private readonly List<GamePlayerGUI> _playerGUIs = new();
        private readonly Dictionary<uint, int> _netIdToGuiIndex = new(); // netId -> GUI list index
        private readonly Dictionary<uint, PlayerEntry> _serverPlayers = new(); 

        private readonly SyncDictionary<uint, PlayerGameInfo> _playerData = new();

        private UnoDeck _deck;

        [SerializeField] private UnoCard _lastTopDiscard;

        [SyncVar(hook = nameof(OnCurrentPlayerChanged))]
        private uint _currentPlayerNetId;

        private int _turnIndex = 0;
        private int _turnDirection = 1;

        private bool _awaitingDrawnCardDecision;

        private void Awake()
        {
            _playerData.OnChange += OnPlayerDataChanged;
        }

        private void OnDestroy()
        {
            _playerData.OnChange -= OnPlayerDataChanged;
        }

        public override void OnStartClient()
        {
            Instance = this;
            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            _cardDrawButton.onClick.AddListener(OnDrawButtonClicked);
            _passTurnButton.onClick.AddListener(OnPassButtonClicked);
            _passTurnButton.gameObject.SetActive(false);
        }

        public override void OnStopClient()
        {
            _cardDrawButton.onClick.RemoveListener(OnDrawButtonClicked);
            _passTurnButton.onClick.RemoveListener(OnPassButtonClicked);
            Instance = null;
        }

        [Client]
        private void OnDrawButtonClicked()
        {
            if (!IsMyTurn()) return;

            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.DrawCard
            });
        }

        [Client]
        private void OnPassButtonClicked()
        {
            if (!_awaitingDrawnCardDecision)
                return;

            _awaitingDrawnCardDecision = false;

            _passTurnButton.gameObject.SetActive(false);

            RefreshHandInteractability(false);

            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.PassTurn
            });
        }

        // ── Server: Setup ─────────────────────────────────────────────────────

        [Server]
        public void AddPlayer(NetworkConnectionToClient conn, PlayerRoomInfo info)
        {
            uint netId = conn.identity.netId;

            _serverPlayers[netId] = new PlayerEntry { Conn = conn, RoomInfo = info };
            _turnOrder.Add(netId);
            _playerData[netId] = new PlayerGameInfo
            {
                connectionId = netId,
                cardCount = 0
            };
        }

        [Server]
        public void StartGame(UnoDeck deck)
        {
            _deck = deck;
            DealCards();
            FlipFirstCard();
            StartCoroutine(InitPanelNextFrame());
        }

        [Server]
        private IEnumerator InitPanelNextFrame()
        {
            yield return null; // wait one frame so SyncDictionary flushes to all clients first
            RpcInitPlayersPanel();
            SetNextTurn(_turnOrder[0]);
        }

        [Server]
        private void DealCards()
        {
            foreach (var (netId, entry) in _serverPlayers)
            {
                var hand = new List<UnoCard>();
                _deck.DrawMultiple(7, hand);

                // Struct must be fully reassigned to trigger SyncDictionary sync
                var data = _playerData[netId];
                data.cardCount = hand.Count;
                _playerData[netId] = data;

                entry.Conn.Send(new ClientDeckMessage
                {
                    clientDeckOperation = ClientDeckOperation.CardDealt,
                    Cards = hand.ToArray(),
                    DrawPileCount = _deck.DrawPileCount
                });
            }
        }

        [Server]
        private void FlipFirstCard()
        {
            UnoCard firstCard;

            // Keep drawing until we get a non-power card
            do
            {
                if (!_deck.TryDraw(out firstCard)) return;

                if (firstCard.Type != CardType.Number)
                {
                    // Put it back and reshuffle before trying again
                    _deck.ReturnToDraw(firstCard);
                    _deck.Shuffle();
                }
                else
                {
                    break;
                }
            } while (true);

            _deck.Discard(firstCard);
            RpcShowTopDiscard(firstCard, _deck.DrawPileCount + 1);
        }

        [Server]
        private void SetNextTurn(uint netID)
        {
            _currentPlayerNetId = netID;
            Debug.Log($"[Turn] Now: {JsonUtility.ToJson(_playerData[netID])} (netId {netID})");
        }

        [Server]
        public void AdvanceTurn()
        {
            _turnIndex = (_turnIndex + _turnDirection + _turnOrder.Count) % _turnOrder.Count;
            SetNextTurn(_turnOrder[_turnIndex]);
        }

        /// <summary>Server: reverse turn direction (Reverse card).</summary>
        [Server]
        public void ReverseTurnDirection()
        {
            _turnDirection *= -1;
        }

        /// <summary>Server: skip next player (Skip card).</summary>
        [Server]
        public void SkipNextPlayer()
        {
            _turnIndex = (_turnIndex + _turnDirection + _turnOrder.Count) % _turnOrder.Count;
        }

        /// <summary>Server: check if this connection is allowed to act.</summary>
        [Server]
        public bool IsCurrentPlayer(NetworkConnectionToClient conn)
        {
            return conn.identity.netId == _currentPlayerNetId;
        }


        [Server]
        public void UpdatePlayerCardCount(NetworkConnectionToClient conn, int newCount)
        {
            uint netId = conn.identity.netId;
            if (!_playerData.ContainsKey(netId)) return;

            var data = _playerData[netId];
            data.cardCount = newCount;
            _playerData[netId] = data;
        }

        // ── Client ────────────────────────────────────────────────────────────

        [Client]
        public void ShowDealtCards(UnoCard[] cards)
        {
            foreach (var card in cards)
            {
                var cardView = Instantiate(_cardPrefab, _handContainer);
                cardView.Setup(card);
            }
        }

        [Client]
        public bool IsMyTurn() => NetworkClient.localPlayer is { netId: var netId } &&
            _currentPlayerNetId == netId;

        [Server]
        public void HandlePlayerCard(NetworkConnectionToClient conn, UnoCard card)
        {
            var netId = conn.identity.netId;

            _deck.Discard(card);

            RpcShowTopDiscard(card, _deck.DrawPileCount + 1);

            var data = _playerData[netId];
            data.cardCount -= 1;
            _playerData[netId] = data;


            conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardPlayed,
                TopDiscardCard = card,
                DrawPileCount = _deck.DrawPileCount
            });

            switch(card.Type)
            {
                case CardType.Reverse:
                    ReverseTurnDirection();
                    AdvanceTurn();
                    break;

                case CardType.Skip:
                    SkipNextPlayer(); // move index past next player
                    AdvanceTurn();    // then advance to the one after
                    break;

                case CardType.DrawTwo:
                    // Give next player 2 cards then skip them
                    //GiveCardsToNextPlayer(2);
                    SkipNextPlayer();
                    AdvanceTurn();
                    break;

                case CardType.WildDrawFour:
                    // Give next player 4 cards then skip them
                    //GiveCardsToNextPlayer(4);
                    SkipNextPlayer();
                    AdvanceTurn();
                    break;

                default:
                    AdvanceTurn();
                    break;
            }
        }

        [Server]
        public void HandleDrawCard(NetworkConnectionToClient conn)
        {
            var netId = conn.identity.netId;

            if (!_deck.TryDraw(out UnoCard drawnCard))
            {
                Debug.LogWarning("[Server] Draw pile empty.");
                return;
            }

            // Update count
            var data = _playerData[netId];
            data.cardCount++;
            _playerData[netId] = data;

            var canPlay = IsPlayableAgainstTop(drawnCard);

            // Send drawn card privately to that player only
            conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardDrawn,
                Cards = new[] { drawnCard },
                DrawPileCount = _deck.DrawPileCount,
                CanPlayDrawnCard = canPlay,
            });

            if(!canPlay)
                AdvanceTurn();

            Debug.Log($"[Server] {data} drew a card. Hand size: {data.cardCount}");
        }

        [Server]
        public void HandlePassTurn(NetworkConnectionToClient conn)
        {
            if (!IsCurrentPlayer(conn)) return;
            Debug.Log("[Server] Player passed after drawing.");
            AdvanceTurn();
        }

        [Client]
        public void OnDrawnCardReceived(bool canPlay, UnoCard drawnCard)
        {
            if(!canPlay)
            {
                RefreshHandInteractability(false);
                return;
            }

            _awaitingDrawnCardDecision = true;
            _passTurnButton.gameObject.SetActive(true);

            foreach (Transform item in _handContainer)
            {
                if (!item.TryGetComponent<Card>(out var card))
                    continue;

                var isDrawnCard = card.CardData.Id == drawnCard.Id;
                var isPlayable = IsValidPlay(card.CardData);
                card.SetInteractable(isDrawnCard && isPlayable);
            }
        }

        [Server]
        private bool IsPlayableAgainstTop(UnoCard drawnCard)
        {
            if(_deck.TopDiscard is not { } topCard)
                return true;

            return drawnCard.Type switch
            {
                CardType.Wild or CardType.WildDrawFour => true,

                _ when drawnCard.Color == topCard.Color => true,
                _ when drawnCard.Type == topCard.Type => true,
                CardType.Number when topCard.Type is CardType.Number
                                 && drawnCard.FaceValue == topCard.FaceValue => true,

                _ => false
            };
        }

        /// <summary>
        /// Builds one GUI row per opponent — skips local player.
        /// </summary>
        [ClientRpc]
        private void RpcInitPlayersPanel()
        {
            foreach (Transform child in _playersContainer)
                Destroy(child.gameObject);
            _playerGUIs.Clear();
            _netIdToGuiIndex.Clear();

            uint selfNetId = NetworkClient.localPlayer.netId;

            foreach (var (netId, info) in _playerData)
            {
                if (netId == selfNetId) continue;

                var gui = Instantiate(_playerGUIPrefab, _playersContainer);
                gui.UpdateCardCount(info.cardCount);

                _netIdToGuiIndex[netId] = _playerGUIs.Count;
                _playerGUIs.Add(gui);
            }
        }

        [ClientRpc]
        public void RpcShowTopDiscard(UnoCard card, int drawCount)
        {
            _lastTopDiscard = card;

            if (Card.CardSprites.TryGetValue(card.Id, out var sprite))
            {
                _topDiscardImage.color = new(1, 1, 1, 1);
                _topDiscardImage.sprite = sprite;
            }

            _drawPileCountText.text = $"{drawCount} left";
        }

        private void OnCurrentPlayerChanged(uint oldNetId, uint newNetId)
        {
            _awaitingDrawnCardDecision = false;
            _passTurnButton.gameObject.SetActive(false);

            if (NetworkClient.localPlayer is not { netId: var selfNetId }) return;

            if (newNetId == selfNetId)
            {
                // My turn — enable playable cards only
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
                RefreshHandInteractability(true);
            }
            else if (oldNetId == selfNetId)
            {
                // My turn ended — block all interaction
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = true; // ← true always, so canvas still catches clicks
                RefreshHandInteractability(false);
            }
        }

        /// <summary>
        /// Fires automatically on every client when any _playerData entry changes.
        /// </summary>
        private void OnPlayerDataChanged(SyncIDictionary<uint, PlayerGameInfo>.Operation op, uint netId, PlayerGameInfo data)
        {
            if (!_netIdToGuiIndex.TryGetValue(netId, out int index))
                return; // self or panel not built yet

            if(_playerData.TryGetValue(netId, out var updatedData))
                _playerGUIs[index].UpdateCardCount(updatedData.cardCount);
        }

        private void RefreshHandInteractability(bool isMyTurn)
        {
            foreach (Transform item in _handContainer)
            {
                if (!item.TryGetComponent<Card>(out var card))
                    continue;

                card.SetInteractable(isMyTurn && IsValidPlay(card.CardData));
            }
        }

        private bool IsValidPlay(UnoCard card)
        {
            var top = _lastTopDiscard;

            if (card.Type == CardType.Wild ||
                card.Type == CardType.WildDrawFour)
                return true;

            if (card.Color == top.Color)
                return true;

            if (card.Type == CardType.Number &&
                top.Type == CardType.Number)
            {
                return card.FaceValue == top.FaceValue;
            }

            return card.Type == top.Type;
        }
    }

    public class PlayerEntry
    {
        public NetworkConnectionToClient Conn;
        public PlayerRoomInfo RoomInfo;
    }
}