using Mirror;
using System;
using UnityEngine;

namespace UNO
{
    public class UnoPlayerController : NetworkBehaviour
    {
        #region PlayCard
        public void TryPlayCard(Card card)
        {
            if (!isLocalPlayer)
                return;

            if (UnoGameController.Instance is not { } Instance)
                return;

            if (!Instance.IsMyTurn())
                return;

            //Instance.ResetDrawnCardDecision();

            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.PlayCard,
                Card = card.CardData
            });

            Destroy(card.gameObject);
        }

        #endregion
    }
}