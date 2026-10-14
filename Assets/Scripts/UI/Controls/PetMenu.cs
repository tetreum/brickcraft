using Brickcraft.Events;
using UnityEngine;

namespace Brickcraft.UI
{
    /// <summary>
    /// A pet's orders, when its owner uses it (see Network.PlayerNetwork.CmdInteractNpc): follow or stay, and
    /// what it attacks, cycled with Change attacks (the popup comes back with the new one).
    /// </summary>
    public class PetMenu : MonoBehaviour
    {
        /// <summary>The Prefabs/UI/Popup prefab.</summary>
        public Popup popupPrefab;

        // what each attack mode is called (Npcs.NpcBrain.PetAttack)
        private static readonly string[] AttackNames = { "nothing", "who hurts you or whom you hit", "that, and hostile NPCs", "that, hostile NPCs and players (with PvP)" };

        private Popup popup;

        private void Awake() {
            EventManager.PetMenuRequested.Subscribe(show);
        }

        private void OnDestroy() {
            EventManager.PetMenuRequested.Unsubscribe(show);
        }

        private void show(PetMenuRequestedEvent pet) {
            if (popup == null) {
                popup = Instantiate(popupPrefab, GetComponentInParent<Canvas>().rootCanvas.transform, false);
                popup.name = "PetPopup";
            }
            popup.transform.SetAsLastSibling();
            DialogMode.Enter();
            string text = (pet.following ? "It follows you." : "It stays where it is.") + " It attacks: " + AttackNames[pet.attacks] + ".";
            string[] buttons = { "Follow", "Stay", "Change attacks" };
            popup.Show(pet.name, text, buttons, false, null, null, (button, input) => {
                DialogMode.Exit();
                if (button < 0 || Player.Instance == null) {
                    return;
                }
                bool following = button == 2 ? pet.following : button == 0;
                byte attacks = button == 2 ? (byte)((pet.attacks + 1) % AttackNames.Length) : pet.attacks;
                Player.Instance.network.CmdPetOrders(pet.npcId, following, attacks);
                if (button == 2) {
                    show(new PetMenuRequestedEvent() { npcId = pet.npcId, name = pet.name, following = following, attacks = attacks });
                }
            });
        }
    }
}
