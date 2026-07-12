using Mirror;
using TMPro;
using UnityEngine;

namespace UNO
{
    public class PlayerGUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _playerName;

        [ClientCallback]
        public void SetPlayerInfo(PlayerRoomInfo info)
        {
            _playerName.text = $"Player {info.playerName}";
            _playerName.color = info.isReady ? Color.green : Color.red;
        }
    }
}