using Mirror;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    public class RoomManager : MonoBehaviour
    {
        [SerializeField] private PlayerGUI _playerPrefab;

        [SerializeField] private GameObject _playerList;
        [SerializeField] private GameObject _leaveGameObject;
        [SerializeField] private GameObject _cancelGameObject;
        [SerializeField] private GameObject _startButtonGameObject;

        [SerializeField] private TextMeshProUGUI _roomCodeText;
        
        [SerializeField] private Button _readyButton;
        private Button _startButton;
        private Button _leaveButton;
        private Button _cancelButton;

        [SerializeField] private bool _owner;

        private Guid _roomCode = Guid.Empty;

        private void Awake()
        {
            _startButton = _startButtonGameObject.GetComponent<Button>();
            _cancelButton = _cancelGameObject.GetComponent<Button>();
            _leaveButton = _leaveGameObject.GetComponent<Button>();
        }

        private void Start()
        {
            _startButton.onClick.AddListener(OnStartButtonClicked);
            _readyButton.onClick.AddListener(OnReadyButtonClicked);
            _cancelButton.onClick.AddListener(OnCancelButtonClicked);
            _leaveButton.onClick.AddListener(OnLeaveButtonClicked);
        }

        private void OnDestroy()
        {
            _startButton.onClick.RemoveListener(OnStartButtonClicked);
            _readyButton.onClick.RemoveListener(OnReadyButtonClicked);
            _cancelButton.onClick.RemoveListener(OnCancelButtonClicked);
            _leaveButton.onClick.RemoveListener(OnLeaveButtonClicked);
        }

        private void OnStartButtonClicked()
        {
            if (!_owner || _roomCode == Guid.Empty) return;

            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Start,
            });
        }

        private void OnReadyButtonClicked()
        {
            if (_roomCode == Guid.Empty)
            {
                Debug.LogWarning("Not in a room");
                return;
            }

            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Ready,
                roomCode = _roomCode
            });
        }

        private void OnCancelButtonClicked()
        {
            if (!_owner || _roomCode == Guid.Empty)
            {
                Debug.LogWarning("Not the room owner");
                return;
            }
            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Cancel
            });
        }

        private void OnLeaveButtonClicked()
        {
            if (_owner || _roomCode == Guid.Empty)
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
        public void RefreshRoomPlayers(PlayerRoomInfo[] playerInfo)
        {
            foreach (Transform child in _playerList.transform)
                Destroy(child.gameObject);

            _startButton.interactable = false;

            var everyoneReady = true;

            foreach (var player in playerInfo)  
            {
                var newPlayer = Instantiate(_playerPrefab, Vector3.zero, Quaternion.identity);
                newPlayer.transform.SetParent(_playerList.transform, false);
                newPlayer.SetPlayerInfo(player);

                if (!player.isReady)
                    everyoneReady = false;
            }
            _startButton.interactable = everyoneReady && _owner && (playerInfo.Length >= 1);
        }

        [ClientCallback]
        public void SetRoomCode(Guid roomCode)
        {
            _roomCode = roomCode;
            var code = roomCode.ToString("N")[..6].ToUpper();
            _roomCodeText.text = $"Room Code: {code}";
        }

        [ClientCallback]
        public void SetOwner(bool owner)
        {
            _owner = owner;
            _cancelGameObject.SetActive(owner);
            _leaveGameObject.SetActive(!owner);
            _startButton.gameObject.SetActive(owner);
        }
    }
}
