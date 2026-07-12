using Mirror;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    public class RoomManager : MonoBehaviour
    {
        [SerializeField] private GameObject _playerList;
        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private GameObject _leaveGameObject;
        [SerializeField] private GameObject _cancelGameObject;

        [SerializeField] private TextMeshProUGUI _roomCodeText;
        [SerializeField] private Button _startButton;
        private Button _cancelButton;
        private Button _leaveButton;

        [SerializeField] private bool _owner;

        private void Awake()
        {
            _cancelButton = _cancelGameObject.GetComponent<Button>();
            _leaveButton = _leaveGameObject.GetComponent<Button>(); 
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
                GameObject newPlayer = Instantiate(_playerPrefab, Vector3.zero, Quaternion.identity);
                newPlayer.transform.SetParent(_playerList.transform, false);
                newPlayer.GetComponent<PlayerGUI>().SetPlayerInfo(player);

                if (!player.isReady)
                    everyoneReady = false;
            }
            _startButton.interactable = everyoneReady && _owner && (playerInfo.Length >= 1);
        }

        [ClientCallback]
        public void SetRoomCode(Guid roomCode)
        {
            var code = roomCode.ToString("N")[..6].ToUpper();
            _roomCodeText.text = $"Room Code: {code}";
        }

        [ClientCallback]
        public void SetOwner(bool owner)
        {
            this._owner = owner;
            _cancelGameObject.SetActive(owner);
            _leaveGameObject.SetActive(!owner);
        }
    }
}
