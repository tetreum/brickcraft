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

        // the keys of what each attack mode is called (Npcs.NpcBrain.PetAttack)
        private static readonly string[] AttackKeys = { "pet.attacks_nothing", "pet.attacks_who_hurts_you", "pet.attacks_and_hostile_npcs", "pet.attacks_and_players" };

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
            string text = Loc.Get(Loc.UI, pet.following ? "pet.follows_attacks" : "pet.stays_attacks", Loc.Get(Loc.UI, AttackKeys[pet.attacks]));
            string[] buttons = { Loc.Get(Loc.UI, "pet.follow"), Loc.Get(Loc.UI, "pet.stay"), Loc.Get(Loc.UI, "pet.change_attacks") };
            popup.Show(pet.name, text, buttons, false, null, null, (button, input) => {
                DialogMode.Exit();
                if (button < 0 || Player.Instance == null) {
                    return;
                }
                bool following = button == 2 ? pet.following : button == 0;
                byte attacks = button == 2 ? (byte)((pet.attacks + 1) % AttackKeys.Length) : pet.attacks;
                Player.Instance.network.CmdPetOrders(pet.npcId, following, attacks);
                if (button == 2) {
                    show(new PetMenuRequestedEvent() { npcId = pet.npcId, name = pet.name, following = following, attacks = attacks });
                }
            });
        }
    }
}
