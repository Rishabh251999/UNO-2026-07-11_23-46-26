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

            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.PlayCard,
                Card = card.CardData
            });

            card.PlayTowards(Instance.CardTargetTransform, Instance._canvas.transform, () => Destroy(card.gameObject));
        }

        #endregion
    }
}