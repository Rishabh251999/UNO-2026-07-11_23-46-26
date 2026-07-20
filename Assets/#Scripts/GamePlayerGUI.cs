
using TMPro;
using UnityEngine;

namespace UNO
{
    public class GamePlayerGUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _cardCountText;

        public void UpdateCardCount(int cardCount)
        {
            Debug.Log($"Updating card count for player {gameObject.name} to {cardCount}");
            _cardCountText.text = $"{cardCount}";
        }
    }
}
