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
        private readonly WaitForSeconds _waitForSeconds0_12 = new(0.12f);

        public static UnoGameController Instance { get; private set; }

        [Header("Script References")]
        [SerializeField] private Card _cardPrefab;

        [Space(5)]

        [Header("GUI References")]
        [SerializeField] private GamePlayerGUI _playerGUIPrefab;
        [Space(2.5f)]
        [SerializeField] private Button _passTurnButton;
        [Space(2.5f)]
        [SerializeField] private Image _topDiscardImage;
        [Space(2.5f)]
        [SerializeField] private TextMeshProUGUI _drawPileCountText;
        [SerializeField] private TextMeshProUGUI _currentPlayerNameText;
        private Button _cardDrawButton;
        private CanvasGroup _canvasGroup;
        private GridLayoutGroup _handGridLayout;

        [Space(5f)]

        [Header("Gameobject References")]
        public GameObject _canvas;
        [SerializeField] private GameObject _cardDrawGameobject;
        [SerializeField] private GameObject _hand;

        [Space(5f)]

        [Header("Transform References")]
        [SerializeField] private Transform _cardTargetTransform;
        private Transform _handContainer;
        public Transform CardTargetTransform => _cardTargetTransform;

        [SerializeField] private int _cardPerPlayer;
        [SerializeField] private float _playMoveDuration = 0.3f;
        public float PlayMoveDuration => _playMoveDuration;    

        private readonly List<uint> _turnOrder = new();
        private readonly List<GamePlayerGUI> _playerGUIs = new();
        private readonly Dictionary<uint, int> _netIdToGuiIndex = new(); // netId -> GUI list index
        private readonly Dictionary<uint, PlayerEntry> _serverPlayers = new(); 

        private readonly SyncDictionary<uint, PlayerGameInfo> _playerData = new();

        private UnoDeck _deck;

        [SyncVar(hook = nameof(OnCurrentPlayerChanged))]
        private uint _currentPlayerNetId;

        [SyncVar(hook = nameof(OnTopDiscardChanged))]
        private UnoCard _syncedTopDiscard;

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

            _canvasGroup = _canvas.GetComponent<CanvasGroup>();
            _cardDrawButton = _cardDrawGameobject.GetComponent<Button>();
            _handGridLayout = _hand.GetComponent<GridLayoutGroup>();

            _handContainer = _hand.transform;

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
                cardCount = 0,
                playerName = info.playerName
            };
        }

        [Server]
        public void StartGame(UnoDeck deck)
        {
            _deck = deck;
            DealCards(_cardPerPlayer);
            FlipFirstCard();
            StartCoroutine(InitPanelNextFrame());
        }

        [Server]
        private IEnumerator InitPanelNextFrame()
        {
            yield return null; // wait one frame so SyncDictionary flushes to all clients first
            SetNextTurn(_turnOrder[0]);
        }

        [Server]
        private void DealCards(int count)
        {
            foreach (var (netId, entry) in _serverPlayers)
            {
                List<UnoCard> hand = new();

                _deck.DrawMultiple(count, hand);

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
            SetTopDiscard(firstCard, _deck.DrawPileCount + 1);
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
        public void ShowDealtCards(UnoCard[] cards, bool applyStartDelay = false)
        {
            StartCoroutine(ShowDealtCardsStaggered(cards, applyStartDelay ? 0.5f : 0f));
        }

        private IEnumerator ShowDealtCardsStaggered(UnoCard[] cards, float startDelay)
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            var views = new List<Card>();

            foreach (var card in cards)
            {
                var cardView = Instantiate(_cardPrefab, _handContainer);
                cardView.Setup(card);
                views.Add(cardView);
            }

            // ONE rebuild with all cards still fully layout-controlled,
            // so every card captures its correct, final grid slot.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_handContainer);

            foreach (var cardView in views)
                cardView.CaptureDealTarget();

            // ONLY NOW do we start excluding cards from layout + snapping to origin.
            foreach (var cardView in views)
                cardView.PrepareDeal(_cardDrawGameobject.transform);

            foreach (var cardView in views)
            {
                cardView.AnimateDeal();
                yield return _waitForSeconds0_12;
            }
        }

        [Client]
        public bool IsMyTurn() => NetworkClient.localPlayer is { netId: var netId } && _currentPlayerNetId == netId;

        [Server]
        public void HandlePlayerCard(NetworkConnectionToClient conn, UnoCard card)
        {
            var netId = conn.identity.netId;

            _deck.Discard(card);

            SetTopDiscard(card, _deck.DrawPileCount + 1);

            var data = _playerData[netId];
            data.cardCount -= 1;
            _playerData[netId] = data;

            conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardPlayed,
                TopDiscardCard = card,
                DrawPileCount = _deck.DrawPileCount
            });

            switch (card.Type)
            {
                case CardType.Reverse:
                    ReverseTurnDirection();
                    AdvanceTurn();
                    break;

                case CardType.Skip:
                    SkipNextPlayer();
                    AdvanceTurn();
                    break;

                case CardType.DrawTwo:
                    AdvanceTurn();
                    break;

                case CardType.WildDrawFour:
                    AdvanceTurn();
                    break;
                        
                default: // Number, Wild
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

            var data = _playerData[netId];
            data.cardCount++;
            _playerData[netId] = data;

            var canPlay = IsPlayableAgainstTop(drawnCard);

            conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardDrawn,
                Cards = new[] { drawnCard },
                DrawPileCount = _deck.DrawPileCount,
                CanPlayDrawnCard = canPlay,
            });

            if (!canPlay)
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


        [ClientRpc]
        private void RpcShowTopDiscard(UnoCard card, int drawCount)
        {
            _syncedTopDiscard = card;

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
                _currentPlayerNameText.SetText($"you");

                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
                RefreshHandInteractability(true);
            }
            else
            {
                if (_playerData.TryGetValue(newNetId, out var currentPlayerInfo))
                    _currentPlayerNameText.SetText(currentPlayerInfo.playerName);

                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = true; // ← true always, so canvas still catches clicks
                RefreshHandInteractability(false);
            }
        }

        private void OnTopDiscardChanged(UnoCard oldCard, UnoCard newCard)
        {
            _syncedTopDiscard = newCard;
        }

        [Server]
        private void SetTopDiscard(UnoCard card, int drawCount)
        {
            _syncedTopDiscard = card;      // SyncVar, always consistent for late/joining clients too
            RpcShowTopDiscard(card, drawCount); // keep RPC only for the visual/GUI update
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

        public void RefreshHandInteractability(bool isMyTurn)
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
            if (card.Type is CardType.Wild ||
                card.Type is CardType.WildDrawFour)
                return true;

            if (card.Color == _syncedTopDiscard.Color)
                return true;

            if (card.Type is CardType.Number &&
                _syncedTopDiscard.Type is CardType.Number)
            {
                return card.FaceValue == _syncedTopDiscard.FaceValue;
            }

            return card.Type == _syncedTopDiscard.Type;
        }
    }

    public class PlayerEntry
    {
        public NetworkConnectionToClient Conn;
        public PlayerRoomInfo RoomInfo;
    }
}