using Brickcraft;
using System.Collections.Generic;
using UnityEngine;

public class TriggerDetector : MonoBehaviour
{
    public List<string> waterBlocks = new List<string>();
    private void OnTriggerEnter(Collider other) {
        
        if (other.tag == "Block" && Server.bricks.ContainsKey(other.name)) {
            if (Server.bricks[other.name].item.layer == (int)Game.Layers.Water) {
                if (waterBlocks.Count < 1) {
                    Player.Instance.isOnWater = true;
                    SoundManager.Instance.play(SoundManager.EFFECT_ENTER_WATER);
                }
                waterBlocks.Add(other.name);
            }
        }
    }
    private void OnTriggerExit(Collider other) {
        if (other.tag == "Block" && Server.bricks.ContainsKey(other.name)) {
            if (Server.bricks[other.name].item.layer == (int)Game.Layers.Water) {
                waterBlocks.Remove(other.name);

                if (waterBlocks.Count < 1) {
                    Player.Instance.isOnWater = false;
                    Player.Instance.firstPersonController.resetGravity();
                }
            }
        }
    }
}
